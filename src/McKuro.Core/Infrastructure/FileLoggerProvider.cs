using System.Text;
using Microsoft.Extensions.Logging;

namespace McKuro.Core.Infrastructure;

/// <summary>
/// 简易文件日志提供器:按类型(category)分目录、按天分文件写入 <c>McKuro-yyyyMMdd.log</c>。
/// 纯托管实现,无反射,兼容 Native AOT;用于本地排查(如极验回调等网络流程)。
/// <para>
/// 性能:每个 category 持有常驻 <see cref="StreamWriter"/>(AutoFlush,跨天自动轮转),
/// 避免每条日志 Directory.CreateDirectory + 文件开/关的系统调用;
/// 清洗后的目录路径按 category 缓存一次,写入按 category 独立加锁(不同类目互不串行)。
/// 文件以 <c>FileShare.ReadWrite</c> 打开,进程运行期间外部工具/测试仍可读。
/// </para>
/// </summary>
public sealed class FileLoggerProvider : ILoggerProvider
{
    private readonly string _dir;
    private readonly LogLevel _minLevel;
    private readonly object _gate = new();
    private readonly Dictionary<string, CategoryWriter> _writers = new(StringComparer.Ordinal);

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
        lock (_gate)
        {
            foreach (var writer in _writers.Values)
            {
                writer.Close();
            }
            _writers.Clear();
        }
    }

    internal bool IsEnabled(LogLevel level) => level >= _minLevel && level != LogLevel.None;

    /// <summary>
    /// 写入日志:按类型(category)分目录,目录内按日期分文件;
    /// 跨天时该类目自动切换到当日新文件,旧文件保留。
    /// </summary>
    internal void Write(LogLevel level, string category, string message, Exception? exception)
    {
        try
        {
            CategoryWriter writer;
            lock (_gate)
            {
                if (!_writers.TryGetValue(category, out writer))
                {
                    writer = new CategoryWriter(category, Path.Combine(_dir, SanitizeCategory(category)));
                    _writers[category] = writer;
                }
            }
            writer.WriteLine(level, message, exception);
        }
        catch (Exception)
        {
            // 日志写入失败不影响主流程
        }
    }

    /// <summary>把名称清洗为安全目录名(取 category 最后一段,过滤非法文件名字符;空则用 "log")。</summary>
    private static string SanitizeCategory(string category)
    {
        var last = string.IsNullOrEmpty(category) ? "" : category.Split('.').LastOrDefault() ?? "";
        var invalid = Path.GetInvalidFileNameChars();
        var safe = new string(last.Where(c => !invalid.Contains(c)).ToArray());
        return safe.Length > 0 ? safe : "log";
    }

    /// <summary>单个 category 的常驻写入器:目录固定,文件按天轮转,自身持锁(类目间无争用)。</summary>
    private sealed class CategoryWriter(string category, string directory)
    {
        private readonly object _writeGate = new();
        private StreamWriter? _writer;
        private string? _date;

        public void WriteLine(LogLevel level, string message, Exception? exception)
        {
            lock (_writeGate)
            {
                var now = DateTime.Now;
                var today = now.ToString("yyyyMMdd");
                if (_writer is null || _date != today)
                {
                    _writer?.Dispose();
                    Directory.CreateDirectory(directory);
                    // FileShare.ReadWrite:常驻句柄不阻止外部读取/尾部查看
                    var stream = new FileStream(
                        Path.Combine(directory, $"McKuro-{today}.log"),
                        FileMode.Append,
                        FileAccess.Write,
                        FileShare.ReadWrite);
                    _writer = new StreamWriter(stream, Encoding.UTF8) { AutoFlush = true };
                    _date = today;
                }
                _writer.WriteLine(
                    $"{now:yyyy-MM-dd HH:mm:ss.fff} [{level,-11}] {category}: {message}");
                if (exception is not null)
                {
                    _writer.WriteLine("    " + exception);
                }
            }
        }

        public void Close()
        {
            lock (_writeGate)
            {
                _writer?.Dispose();
                _writer = null;
                _date = null;
            }
        }
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
