using McKuro.Core.Services.Settings;
using McKuro.Services;

namespace McKuro.Services;

/// <summary>
/// 每日自动任务调度器:应用启动 15 秒后(待网络与账号初始化)执行一次
/// 游戏签到与库街区每日任务,按天去重 —— 当天已执行过则跳过(反复重启不重复)。
/// 手动执行不受限制(签到页「一键日常」)。
/// </summary>
public sealed class DailyTaskScheduler : IDisposable
{
    private readonly CancellationTokenSource _cts = new();
    private Task? _loop;

    /// <summary>启动后首次执行的延迟(秒)。提醒调度器会 <see cref="WaitForFirstRunAsync"/> 等它跑完。</summary>
    public static readonly TimeSpan StartupDelay = TimeSpan.FromSeconds(15);

    /// <summary>本次启动的自动签到任务已完成(提醒侧据此避免"刚签完就误报未签到")。</summary>
    private readonly TaskCompletionSource _firstRunDone = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public void Start()
    {
        if (_loop is not null)
        {
            return;
        }
        _loop = Task.Run(() => RunOnceAfterStartupAsync(_cts.Token));
    }

    /// <summary>
    /// 等本次启动的自动签到跑完(带超时兜底)。供提醒调度器在检查"游戏签到状态"前调用:
    /// 即使把首轮提醒延迟调短,也不会在签到进行中就把账号报成"未签到"。
    /// </summary>
    public async Task WaitForFirstRunAsync(TimeSpan timeout)
    {
        var completed = await Task.WhenAny(_firstRunDone.Task, Task.Delay(timeout)).ConfigureAwait(false);
        System.Console.Error.WriteLine(completed == _firstRunDone.Task
            ? "MCKURO-REMINDER sign-check: 自动签到已结束(等到了)"
            : $"MCKURO-REMINDER sign-check: 等自动签到超时({timeout.TotalSeconds:F0}s),继续检查");
    }

    private async Task RunOnceAfterStartupAsync(CancellationToken ct)
    {
        try
        {
            await Task.Delay(StartupDelay, ct).ConfigureAwait(false);
            await TryRunDailyTasksAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // 正常退出
        }
        finally
        {
            // 无论成功/失败/跳过都要放行提醒侧,避免它一直等
            _firstRunDone.TrySetResult();
        }
    }

    private async Task TryRunDailyTasksAsync(CancellationToken ct)
    {
        var settings = AppServices.Settings.Current;
        var account = AppServices.KuroAccounts.Current;
        if (account is null)
        {
            return;
        }

        if (!settings.AutoSignEnabled && !settings.AutoKuroClientTaskEnabled)
        {
            return;
        }

        var today = DailyAutoRunSchedule.TodayText(DateTime.Now);
        if (!DailyAutoRunSchedule.ShouldRunNow(DateTime.Now, settings.LastDailyAutoRunDate))
        {
            return;
        }

        // 先记录执行日期再执行:保证一天最多一次,失败也不在当日反复重试轰炸接口
        settings.LastDailyAutoRunDate = today;
        AppServices.Settings.Save();
        System.Console.Error.WriteLine(
            $"MCKURO-DAILY auto: start sign={settings.AutoSignEnabled} bbsTask={settings.AutoKuroClientTaskEnabled}");

        if (settings.AutoSignEnabled)
        {
            await AppServices.KuroSign.SignAllGamesAsync(account, ct).ConfigureAwait(false);
        }
        if (settings.AutoKuroClientTaskEnabled)
        {
            await AppServices.KuroSign.ExecuteDailyTasksAsync(account, ct).ConfigureAwait(false);
        }
        System.Console.Error.WriteLine("MCKURO-DAILY auto: done");
    }

    public void Dispose()
    {
        _cts.Cancel();
        _cts.Dispose();
    }
}
