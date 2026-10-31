using McKuro.Core.Services.Settings;
using McKuro.Services;
using Microsoft.Extensions.Logging;

namespace McKuro.Services;

/// <summary>
/// 每日自动签到调度器:应用启动 15 秒后(待网络与账号初始化)检查一次,之后每 <see cref="CheckInterval"/>
/// 再检查一次(托盘常驻跨天后也能签到);按天去重 —— 当天已执行过则跳过(反复重启不重复)。
/// 库街区每日任务(浏览/点赞/分享)不参与自动调度,只能在签到页手动执行。
/// </summary>
public sealed class DailyTaskScheduler : IDisposable
{
    private readonly CancellationTokenSource _cts = new();
    private Task? _loop;

    /// <summary>启动后首次执行的延迟(秒)。</summary>
    public static readonly TimeSpan StartupDelay = TimeSpan.FromSeconds(15);

    /// <summary>之后的检查间隔:跨天后最多晚这么久签到(未到新的一天时检查只读设置,无网络请求)。</summary>
    public static readonly TimeSpan CheckInterval = TimeSpan.FromHours(1);

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
            await Task.Delay(StartupDelay, ct).ConfigureAwait(false);
            using var timer = new PeriodicTimer(CheckInterval);
            do
            {
                try
                {
                    await TryRunDailySignAsync(ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    // 单次失败(网络/接口/保存设置)只记日志,循环继续;当天已标记则次日再试
                    Log()?.LogWarning(ex, "每日自动签到失败");
                }
            }
            while (await timer.WaitForNextTickAsync(ct).ConfigureAwait(false));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // 正常退出
        }
        catch (Exception ex)
        {
            Log()?.LogWarning(ex, "每日自动签到调度循环异常退出");
        }
    }

    /// <summary>日志(容器未初始化时为 null)。</summary>
    private static ILogger? Log()
        => AppServices.LoggerFactory?.CreateLogger<DailyTaskScheduler>();

    private async Task TryRunDailySignAsync(CancellationToken ct)
    {
        var settings = AppServices.Settings.Current;
        var account = AppServices.KuroAccounts.Current;
        if (account is null)
        {
            return;
        }

        if (!settings.AutoSignEnabled)
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
        System.Console.Error.WriteLine("MCKURO-DAILY auto: start sign");

        await AppServices.KuroSign.SignAllGamesAsync(account, ct).ConfigureAwait(false);
        System.Console.Error.WriteLine("MCKURO-DAILY auto: done");
    }

    public void Dispose()
    {
        _cts.Cancel();
        _cts.Dispose();
    }
}
