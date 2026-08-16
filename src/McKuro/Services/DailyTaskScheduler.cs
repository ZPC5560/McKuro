using McKuro.Services;

namespace McKuro.Services;

/// <summary>
/// 每日自动任务调度器:每天 8:00 执行游戏签到与库街区每日任务;
/// 应用启动后 15 秒内执行一次(登录态存在且开关开启时)。
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
            // 启动后延迟 15 秒执行一次
            await Task.Delay(TimeSpan.FromSeconds(15), ct).ConfigureAwait(false);
            await TryRunDailyTasksAsync(ct).ConfigureAwait(false);

            // 之后每天 8:00 执行
            while (!ct.IsCancellationRequested)
            {
                var now = DateTime.Now;
                var next = now.Date.AddHours(8);
                if (next <= now)
                {
                    next = next.AddDays(1);
                }
                await Task.Delay(next - now, ct).ConfigureAwait(false);
                await TryRunDailyTasksAsync(ct).ConfigureAwait(false);
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

        if (settings.AutoSignEnabled)
        {
            await AppServices.KuroSign.SignAllGamesAsync(account, ct).ConfigureAwait(false);
        }
        if (settings.AutoKuroClientTaskEnabled)
        {
            await AppServices.KuroSign.ExecuteDailyTasksAsync(account, ct).ConfigureAwait(false);
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
        _cts.Dispose();
    }
}
