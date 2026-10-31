using System.Diagnostics;
using Avalonia.Threading;

namespace McKuro.Services;

/// <summary>游戏会话状态:空闲 / 启动中 / 游戏中。</summary>
public enum GameSessionState
{
    /// <summary>未启动(按钮显示「启动游戏」)。</summary>
    Idle,

    /// <summary>已点击启动,等待游戏进程稳定(按钮显示「启动中」)。</summary>
    Launching,

    /// <summary>游戏进程启动后持续运行满 20 秒(按钮显示「游戏中」)。</summary>
    InGame,
}

/// <summary>游戏会话结束原因(仅状态从运行中回到空闲时触发)。</summary>
public enum GameSessionEndReason
{
    /// <summary>未到「游戏中」进程就退出了(视为启动失败)。</summary>
    Failed,

    /// <summary>已进入「游戏中」后进程正常退出。</summary>
    Finished,
}

/// <summary>
/// 游戏进程监控(对齐 Haiyu gameRunTimer 3 秒轮询思路):
/// 点击启动后每 2 秒轮询一次游戏进程,进程持续存活满 <see cref="InGameWindow"/>(默认 20 秒)进入「游戏中」;
/// 进程消失时回到「空闲」,并按结束原因触发 <see cref="SessionEnded"/>。
/// 进程探测(<c>Process.GetProcessesByName</c>,常见 10-50 ms)在计时器线程执行、不持锁;
/// 只把探测结果投递到 UI 线程做状态判定,事件一律在 UI 线程上触发。
/// </summary>
public sealed class GameProcessMonitor : IDisposable
{
    /// <summary>启动后游戏进程稳定窗口:持续运行这么久不退出即视为「游戏中」(可注入覆盖)。</summary>
    public static readonly TimeSpan DefaultInGameWindow = TimeSpan.FromSeconds(20);

    private readonly Func<IReadOnlyList<string>, bool> _isAlive;
    private readonly Func<DateTime> _now;
    private readonly TimeSpan _inGameWindow;
    private readonly TimeSpan _pollInterval;
    private readonly System.Threading.Timer? _timer;
    private readonly object _gate = new();

    private IReadOnlyList<string> _names = [];
    private DateTime _launchedAt = DateTime.MinValue;
    private GameSessionState _state = GameSessionState.Idle;

    /// <summary>本次会话是否探测到过进程存活(区分「从未出现」与「出现后又退出」)。</summary>
    private bool _everAlive;

    /// <summary>会话代号:BeginLaunch/Reset 时自增,丢弃会话切换前发起的后台探测结果。仅在 <see cref="_gate"/> 内读写。</summary>
    private int _session;

    /// <summary>计时器回调重入保护:上一拍探测还没结束时跳过本拍(1 = 探测中)。</summary>
    private int _polling;

    /// <summary>状态切换(Idle/Launching/InGame),UI 线程触发。</summary>
    public event Action<GameSessionState>? StateChanged;

    /// <summary>游戏会话在一次监控周期内结束(启动失败或已玩游戏退出),UI 线程触发。</summary>
    public event Action<GameSessionEndReason>? SessionEnded;

    /// <summary>当前状态。</summary>
    public GameSessionState State
    {
        get
        {
            lock (_gate)
            {
                return _state;
            }
        }
    }

    /// <param name="isAlive">进程存活探测(可注入测试;默认按进程名探测)。</param>
    /// <param name="now">时钟(可注入测试;默认 DateTime.Now)。</param>
    /// <param name="inGameWindow">「游戏中」稳定窗口(默认 20 秒)。</param>
    /// <param name="pollInterval">轮询间隔(默认 2 秒)。</param>
    /// <param name="startTimer">是否启动轮询计时器(测试传 false,手动调用 <see cref="Tick"/>)。</param>
    public GameProcessMonitor(
        Func<IReadOnlyList<string>, bool>? isAlive = null,
        Func<DateTime>? now = null,
        TimeSpan? inGameWindow = null,
        TimeSpan? pollInterval = null,
        bool startTimer = true)
    {
        _isAlive = isAlive ?? ProbeProcesses;
        _now = now ?? (() => DateTime.Now);
        _inGameWindow = inGameWindow ?? DefaultInGameWindow;
        _pollInterval = pollInterval ?? TimeSpan.FromSeconds(2);
        if (startTimer)
        {
            // 空闲期不轮询:计时器挂起,BeginLaunch 才启动,回 Idle 即停(避免每 2s 空 Post 到 UI 线程)。
            _timer = new System.Threading.Timer(
                _ => PollOnTimerThread(),
                null,
                Timeout.InfiniteTimeSpan,
                _pollInterval);
        }
    }

    /// <summary>
    /// 开始一次游戏会话监控(点击「启动游戏」成功后调用)。
    /// <paramref name="processNames"/> 为候选进程名(不含扩展名,命中任意一个即视为游戏进程存活)。
    /// </summary>
    public void BeginLaunch(IReadOnlyList<string> processNames)
    {
        var names = processNames
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        lock (_gate)
        {
            _names = names;
            _launchedAt = _now();
            _state = GameSessionState.Launching;
            _everAlive = false;
            _session++;
        }
        _timer?.Change(_pollInterval, _pollInterval);
        StateChanged?.Invoke(GameSessionState.Launching);
    }

    /// <summary>
    /// 计时器线程:取快照后在锁外做进程探测,结果投递到 UI 线程判定(UI 线程不再被探测卡住)。
    /// </summary>
    private void PollOnTimerThread()
    {
        if (Interlocked.Exchange(ref _polling, 1) == 1)
        {
            return; // 上一拍探测仍在进行(系统繁忙时 GetProcessesByName 可能超过轮询间隔)
        }
        try
        {
            if (!TrySnapshot(out var names, out var session))
            {
                return;
            }
            var alive = _isAlive(names);
            Dispatcher.UIThread.Post(() => Apply(alive, session));
        }
        catch (Exception)
        {
            // 探测异常:跳过本拍,下一拍再试
        }
        finally
        {
            Volatile.Write(ref _polling, 0);
        }
    }

    /// <summary>
    /// 轮询一拍(同步版,测试可直接调用):
    /// 启动中:进程存活且距启动满稳定窗口 → 游戏中;进程消失 → 空闲(启动失败)。
    /// 游戏中:进程消失 → 空闲(正常结束)。
    /// </summary>
    public void Tick()
    {
        if (!TrySnapshot(out var names, out var session))
        {
            return;
        }
        Apply(_isAlive(names), session);
    }

    /// <summary>取当前会话的进程名与会话代号;空闲时返回 false。</summary>
    private bool TrySnapshot(out IReadOnlyList<string> names, out int session)
    {
        lock (_gate)
        {
            names = _names;
            session = _session;
            return _state is not GameSessionState.Idle && _names.Count > 0;
        }
    }

    /// <summary>按探测结果推进状态机(UI 线程);会话已切换(BeginLaunch/Reset)的过期结果直接丢弃。</summary>
    private void Apply(bool alive, int session)
    {
        GameSessionState next;
        GameSessionEndReason? ended = null;
        lock (_gate)
        {
            if (session != _session || _state is GameSessionState.Idle || _names.Count == 0)
            {
                return;
            }

            var elapsed = _now() - _launchedAt;
            if (alive)
            {
                _everAlive = true;
            }

            if (_state == GameSessionState.Launching)
            {
                if (alive && elapsed >= _inGameWindow)
                {
                    _state = GameSessionState.InGame;
                    next = GameSessionState.InGame;
                }
                else if (!alive && (elapsed >= _inGameWindow || _everAlive))
                {
                    // 启动窗口内进程消失(或从未在窗口内出现):启动失败
                    _state = GameSessionState.Idle;
                    next = GameSessionState.Idle;
                    ended = GameSessionEndReason.Failed;
                    _timer?.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
                }
                else
                {
                    return;
                }
            }
            else // InGame
            {
                if (!alive)
                {
                    _state = GameSessionState.Idle;
                    next = GameSessionState.Idle;
                    ended = GameSessionEndReason.Finished;
                    _timer?.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
                }
                else
                {
                    return;
                }
            }
        }

        StateChanged?.Invoke(next);
        if (ended is { } reason)
        {
            SessionEnded?.Invoke(reason);
        }
    }

    /// <summary>取消当前监控并回到空闲(不触发 SessionEnded)。</summary>
    public void Reset()
    {
        lock (_gate)
        {
            _state = GameSessionState.Idle;
            _names = [];
            _session++;
        }
        _timer?.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        StateChanged?.Invoke(GameSessionState.Idle);
    }

    /// <summary>
    /// 冒烟专用:伪造一次会话结束(走与真实流程完全相同的事件出口)。
    /// <para>
    /// 为什么需要:验证「游戏已退出」这类状态文案的自动消失,必须让
    /// <see cref="SessionEnded"/> 真的触发一次;而真实触发要等游戏启动满 <see cref="DefaultInGameWindow"/>
    /// 再退出进程,自动化脚本既慢又依赖真游戏。这里只发事件、不改内部状态机,
    /// 因此不会污染"空闲/启动中/游戏中"的常规判定。
    /// </para>
    /// </summary>
    public void SimulateSessionEndedForSmoke(GameSessionEndReason reason) => SessionEnded?.Invoke(reason);

    /// <summary>默认存活探测:任一进程名命中即视为游戏进程存在。</summary>
    private static bool ProbeProcesses(IReadOnlyList<string> names)
    {
        foreach (var name in names)
        {
            Process[] procs;
            try
            {
                procs = Process.GetProcessesByName(name);
            }
            catch (Exception)
            {
                // 单名探测失败不影响其余进程名
                continue;
            }

            // GetProcessesByName 返回的对象持有原生句柄:必须逐个 Dispose,
            // 否则每轮 2 秒轮询泄漏一批句柄,只能等终结器慢慢回收。
            var found = procs.Length > 0;
            foreach (var p in procs)
            {
                p.Dispose();
            }
            if (found)
            {
                return true;
            }
        }
        return false;
    }

    public void Dispose()
    {
        _timer?.Dispose();
    }
}
