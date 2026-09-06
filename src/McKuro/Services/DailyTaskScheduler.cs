using McKuro.Core.Services.Settings;
using McKuro.Services;

namespace McKuro.Services;

/// <summary>
/// 每日自动任务调度器:每天到达设定时间(设置 DailyAutoRunTime,默认 08:00)后执行一次
/// 游戏签到与库街区每日任务;当天已执行过则跳过(反复重启不重复),应用错过设定时间时
/// 下次启动补执行一次。每 30 秒轮询检查,修改执行时间后自动生效。
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
        _loop = Task.Run(() => RunLoopAsync(_cts.Token));
    }

    private async Task RunLoopAsync(CancellationToken ct)
    {
        try
        {
            // 启动后延迟 15 秒再开始检查(等网络与账号初始化完成)
            await Task.Delay(TimeSpan.FromSeconds(15), ct).ConfigureAwait(false);
            while (!ct.IsCancellationRequested)
            {
                await TryRunDailyTasksAsync(ct).ConfigureAwait(false);
                await Task.Delay(TimeSpan.FromSeconds(30), ct).ConfigureAwait(false);
            }
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

        var time = DailyAutoRunSchedule.TryParseTime(settings.DailyAutoRunTime, out var parsed)
            ? parsed
            : TimeSpan.FromHours(8);
        if (!DailyAutoRunSchedule.ShouldRunNow(DateTime.Now, time, settings.LastDailyAutoRunDate))
        {
            return;
        }

        // 先记录执行日期再执行:保证一天最多一次,失败也不在当日反复重试轰炸接口
        settings.LastDailyAutoRunDate = DateTime.Now.ToString("yyyy-MM-dd");
        AppServices.Settings.Save();
        System.Console.Error.WriteLine(
            $"MCKURO-DAILY auto: start at {settings.DailyAutoRunTime} sign={settings.AutoSignEnabled} bbsTask={settings.AutoKuroClientTaskEnabled}");

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
