using McKuro.Services;

namespace McKuro.Tests;

/// <summary>
/// 游戏进程监控状态机:启动中 → 20 秒稳定 → 游戏中 → 进程消失回空闲。
/// 使用可注入的进程探测与时钟,直接调用 <see cref="GameProcessMonitor.Tick"/> 驱动。
/// </summary>
public sealed class GameProcessMonitorTests
{
    private readonly List<string> _names = ["Client-Win64-Shipping", "Wuthering Waves"];

    private static (GameProcessMonitor Monitor, ManualClock Clock, Tracking Probe) Create(
        TimeSpan? inGameWindow = null)
    {
        var clock = new ManualClock();
        var probe = new Tracking();
        var monitor = new GameProcessMonitor(
            isAlive: probe.IsAlive,
            now: () => clock.Now,
            inGameWindow: inGameWindow ?? TimeSpan.FromSeconds(20),
            startTimer: false);
        return (monitor, clock, probe);
    }

    [Fact]
    public void BeginLaunch_升起启动中状态()
    {
        var (monitor, _, probe) = Create();
        var states = new List<GameSessionState>();
        monitor.StateChanged += states.Add;

        monitor.BeginLaunch(_names);
        monitor.Tick(); // 游戏进程尚未出现

        Assert.Equal(GameSessionState.Launching, monitor.State);
        Assert.Equal([GameSessionState.Launching], states);
        Assert.Equal(_names, probe.LastQueriedNames);
    }

    [Fact]
    public void 进程存活满20秒_进入游戏中()
    {
        var (monitor, clock, probe) = Create();
        monitor.BeginLaunch(_names);
        probe.Alive = true;

        clock.Advance(TimeSpan.FromSeconds(19));
        monitor.Tick();
        Assert.Equal(GameSessionState.Launching, monitor.State);

        clock.Advance(TimeSpan.FromSeconds(1));
        monitor.Tick();
        Assert.Equal(GameSessionState.InGame, monitor.State);

        // 游戏中进程仍在 → 保持游戏中
        clock.Advance(TimeSpan.FromMinutes(5));
        monitor.Tick();
        Assert.Equal(GameSessionState.InGame, monitor.State);
    }

    [Fact]
    public void 启动窗口内进程消失_视为启动失败()
    {
        var (monitor, clock, probe) = Create();
        var states = new List<GameSessionState>();
        var reasons = new List<GameSessionEndReason>();
        monitor.StateChanged += states.Add;
        monitor.SessionEnded += reasons.Add;

        monitor.BeginLaunch(_names);
        probe.Alive = true;

        clock.Advance(TimeSpan.FromSeconds(10));
        monitor.Tick();
        Assert.Equal(GameSessionState.Launching, monitor.State);

        probe.Alive = false;
        clock.Advance(TimeSpan.FromSeconds(1));
        monitor.Tick();

        Assert.Equal(GameSessionState.Idle, monitor.State);
        Assert.Equal([GameSessionEndReason.Failed], reasons);
        Assert.Contains(GameSessionState.Idle, states);
        // 失败后不再继续监测
        monitor.Tick();
        Assert.Single(reasons);
    }

    [Fact]
    public void 游戏中进程消失_正常结束()
    {
        var (monitor, clock, probe) = Create();
        var states = new List<GameSessionState>();
        var reasons = new List<GameSessionEndReason>();
        monitor.StateChanged += states.Add;
        monitor.SessionEnded += reasons.Add;

        monitor.BeginLaunch(_names);
        probe.Alive = true;
        clock.Advance(TimeSpan.FromSeconds(20));
        monitor.Tick();
        Assert.Equal(GameSessionState.InGame, monitor.State);

        probe.Alive = false;
        clock.Advance(TimeSpan.FromSeconds(2));
        monitor.Tick();

        Assert.Equal(GameSessionState.Idle, monitor.State);
        Assert.Equal([GameSessionEndReason.Finished], reasons);
        Assert.Equal(GameSessionState.InGame, states[1]);
        Assert.Equal(GameSessionState.Idle, states[2]);
    }

    [Fact]
    public void 进程从未出现且超时_判定失败()
    {
        var (monitor, clock, _) = Create();
        monitor.BeginLaunch(_names);
        var reasons = new List<GameSessionEndReason>();
        monitor.SessionEnded += reasons.Add;

        clock.Advance(TimeSpan.FromSeconds(30));
        monitor.Tick();

        Assert.Equal(GameSessionState.Idle, monitor.State);
        Assert.Equal([GameSessionEndReason.Failed], reasons);
    }

    [Fact]
    public void BeginLaunch_忽略空进程名并去重()
    {
        var (monitor, _, probe) = Create();
        monitor.BeginLaunch(["A", "", " ", "A", "b"]);
        probe.Alive = true;
        monitor.Tick(); // 触发一次探测,验证实际传入的进程名列表

        Assert.Equal(GameSessionState.Launching, monitor.State);
        Assert.Equal(["A", "b"], probe.LastQueriedNames);
    }

    [Fact]
    public void Reset_回到空闲且不触发会话结束()
    {
        var (monitor, clock, probe) = Create();
        var reasons = new List<GameSessionEndReason>();
        var states = new List<GameSessionState>();
        monitor.StateChanged += states.Add;
        monitor.SessionEnded += reasons.Add;

        monitor.BeginLaunch(_names);
        probe.Alive = true;
        clock.Advance(TimeSpan.FromSeconds(25));
        monitor.Tick();
        Assert.Equal(GameSessionState.InGame, monitor.State);

        monitor.Reset();

        Assert.Equal(GameSessionState.Idle, monitor.State);
        Assert.Empty(reasons);
        Assert.Equal(GameSessionState.Idle, states[^1]);
    }

    private sealed class ManualClock
    {
        public DateTime Now { get; private set; } = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

        public void Advance(TimeSpan delta) => Now += delta;
    }

    private sealed class Tracking
    {
        public bool Alive { get; set; }

        public IReadOnlyList<string> LastQueriedNames { get; private set; } = [];

        public bool IsAlive(IReadOnlyList<string> names)
        {
            LastQueriedNames = names;
            return Alive;
        }
    }
}
