using System.Text;
using Microsoft.Extensions.Logging;

namespace McKuro.Core.Infrastructure;

/// <summary>
/// 简易文件日志提供器:按天写入 <c>McKuro-yyyyMMdd.log</c>。
/// 纯托管实现,无反射,兼容 Native AOT;用于本地排查(如极验回调等网络流程)。
/// </summary>
public sealed class FileLoggerProvider : ILoggerProvider
{
    private readonly string _dir;
    private readonly LogLevel _minLevel;
    private readonly object _gate = new();

    public FileLoggerProvider(string logDirectory, LogLevel minLevel = LogLevel.Information)
    {
        _dir = logDirectory;
        _minLevel = minLevel;
        try
        {
            Directory.CreateDirectory(_dir);
        }
        catch (Exception)
        {
            // 目录创建失败时静默降级(日志功能不阻断主流程)
        }
    }

    public string LogDirectory => _dir;

    public ILogger CreateLogger(string categoryName) => new FileLogger(this, categoryName);

    public void Dispose()
    {
    }

    internal bool IsEnabled(LogLevel level) => level >= _minLevel && level != LogLevel.None;

    /// <summary>
    /// 写入日志:按类型(category)分目录,目录内按日期分文件;
    /// 每次写入实时计算日期路径,跨天自动新建当日文件,旧文件保留。
    /// </summary>
    internal void Write(LogLevel level, string category, string message, Exception? exception)
    {
        try
        {
            var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level,-11}] {category}: {message}";
            if (exception is not null)
            {
                line += Environment.NewLine + "    " + exception;
            }

            lock (_gate)
            {
                // 类型目录:取 category 最后一段(如 McKuro.SmsLogin → SmsLogin),过滤非法文件名字符
                var dir = Path.Combine(_dir, SanitizeCategory(category));
                Directory.CreateDirectory(dir);
                // 日期文件:McKuro-yyyyMMdd.log,跨天自动切换
                var path = Path.Combine(dir, $"McKuro-{DateTime.Now:yyyyMMdd}.log");
                File.AppendAllText(path, line + Environment.NewLine, Encoding.UTF8);
            }
        }
        catch (Exception)
        {
            // 日志写入失败不影响主流程
        }
    }

    /// <summary>把 category 转成安全的目录名(取最后一段,过滤非法字符;空则用 "log")。</summary>
    private static string SanitizeCategory(string category)
    {
        var last = string.IsNullOrEmpty(category) ? "" : category.Split('.').LastOrDefault() ?? "";
        var invalid = Path.GetInvalidFileNameChars();
        var safe = new string(last.Where(c => !invalid.Contains(c)).ToArray());
        return safe.Length > 0 ? safe : "log";
    }

    private sealed class FileLogger : ILogger
    {
        private readonly FileLoggerProvider _provider;
        private readonly string _category;

        public FileLogger(FileLoggerProvider provider, string category)
        {
            _provider = provider;
            _category = category;
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => _provider.IsEnabled(logLevel);

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
            {
                return;
            }
            _provider.Write(logLevel, _category, formatter(state, exception), exception);
        }
    }
}
