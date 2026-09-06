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

    public void Start()
    {
        if (_loop is not null)
        {
            return;
        }
        _loop = Task.Run(() => RunOnceAfterStartupAsync(_cts.Token));
    }

    private async Task RunOnceAfterStartupAsync(CancellationToken ct)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(15), ct).ConfigureAwait(false);
            await TryRunDailyTasksAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // 正常退出
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
