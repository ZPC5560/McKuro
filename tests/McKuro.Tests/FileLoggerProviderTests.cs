using McKuro.Core.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace McKuro.Tests;

/// <summary>文件日志提供器测试(本地排查日志,按类型分目录 + 按日期分文件)。</summary>
public class FileLoggerProviderTests
{
    /// <summary>
    /// 共享读:provider 对日志文件持有常驻写句柄(FileShare.ReadWrite),
    /// File.ReadAllText 的 FileShare.Read 与写访问冲突,测试须以 ReadWrite 共享模式读取。
    /// </summary>
    private static string ReadShared(string path)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var sr = new StreamReader(fs, System.Text.Encoding.UTF8);
        return sr.ReadToEnd();
    }

    [Fact]
    public void Writes_Log_To_Category_Dir_With_Date_File()
    {
        var dir = Path.Combine(Path.GetTempPath(), "mckuro-log-" + Guid.NewGuid().ToString("N"));
        try
        {
            using var provider = new FileLoggerProvider(dir);
            var logger = provider.CreateLogger("McKuro.SmsLogin");
            logger.LogInformation("hello {Name} {Num}", "world", 42);
            logger.LogWarning("warn message");

            // 按类型分目录:McKuro.SmsLogin → SmsLogin 子目录
            var categoryDir = Path.Combine(dir, "SmsLogin");
            Assert.True(Directory.Exists(categoryDir), "应按类型创建子目录");

            // 按日期分文件:McKuro-yyyyMMdd.log
            var file = Directory.GetFiles(categoryDir, $"McKuro-{DateTime.Now:yyyyMMdd}.log").Single();
            var content = ReadShared(file);
            Assert.Contains("[Information]", content);
            Assert.Contains("McKuro.SmsLogin: hello world 42", content);
            Assert.Contains("[Warning", content);
            Assert.Contains("McKuro.SmsLogin: warn message", content);
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { }
        }
    }

    [Fact]
    public void Different_Categories_Go_To_Different_Dirs()
    {
        var dir = Path.Combine(Path.GetTempPath(), "mckuro-log-" + Guid.NewGuid().ToString("N"));
        try
        {
            using var provider = new FileLoggerProvider(dir);
            provider.CreateLogger("McKuro.SmsLogin").LogInformation("sms log");
            provider.CreateLogger("McKuro.Services.GeetVerifyService").LogInformation("geet log");
            provider.CreateLogger("McKuro.Core.Services.Game.GameUpdater").LogInformation("update log");

            Assert.True(Directory.Exists(Path.Combine(dir, "SmsLogin")));
            Assert.True(Directory.Exists(Path.Combine(dir, "GeetVerifyService")));
            Assert.True(Directory.Exists(Path.Combine(dir, "GameUpdater")));

            Assert.Contains("sms log",
                ReadShared(Directory.GetFiles(Path.Combine(dir, "SmsLogin"), "*.log").Single()));
            Assert.Contains("geet log",
                ReadShared(Directory.GetFiles(Path.Combine(dir, "GeetVerifyService"), "*.log").Single()));
            Assert.Contains("update log",
                ReadShared(Directory.GetFiles(Path.Combine(dir, "GameUpdater"), "*.log").Single()));
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { }
        }
    }

    [Fact]
    public void Writes_Exception_Details()
    {
        var dir = Path.Combine(Path.GetTempPath(), "mckuro-log-" + Guid.NewGuid().ToString("N"));
        try
        {
            using var provider = new FileLoggerProvider(dir);
            provider.CreateLogger("McKuro.Test").LogError(
                new InvalidOperationException("boom"), "failed op");

            var file = Directory.GetFiles(Path.Combine(dir, "Test"), "*.log").Single();
            var content = ReadShared(file);
            Assert.Contains("[Error", content);
            Assert.Contains("failed op", content);
            Assert.Contains("InvalidOperationException", content);
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { }
        }
    }

    [Fact]
    public void Below_MinLevel_Is_Not_Written()
    {
        var dir = Path.Combine(Path.GetTempPath(), "mckuro-log-" + Guid.NewGuid().ToString("N"));
        try
        {
            using var provider = new FileLoggerProvider(dir, LogLevel.Warning);
            var logger = provider.CreateLogger("McKuro.Test");
            logger.LogInformation("should not appear");
            logger.LogError("should appear");

            var file = Directory.GetFiles(Path.Combine(dir, "Test"), "*.log").Single();
            var content = ReadShared(file);
            Assert.DoesNotContain("should not appear", content);
            Assert.Contains("should appear", content);
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { }
        }
    }

    [Fact]
    public void Same_Date_Writes_Append_To_Same_File()
    {
        var dir = Path.Combine(Path.GetTempPath(), "mckuro-log-" + Guid.NewGuid().ToString("N"));
        try
        {
            using var provider = new FileLoggerProvider(dir);
            var logger = provider.CreateLogger("McKuro.Test");
            logger.LogInformation("first");
            logger.LogInformation("second");

            var files = Directory.GetFiles(Path.Combine(dir, "Test"), "*.log");
            Assert.Single(files); // 同日只生成一个文件
            var content = ReadShared(files[0]);
            Assert.Contains("first", content);
            Assert.Contains("second", content);
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { }
        }
    }

    [Fact]
    public void Di_Injected_Logger_With_Real_Factory_Writes_File()
    {
        // 回归:AppServices 必须把真实 LoggerFactory 传入 RegisterCore,
        // 否则 DI 注入的 ILogger<T>(如 GeetVerifyService) 是 NullLogger,不写任何日志
        var dataDir = Path.Combine(Path.GetTempPath(), "mckuro-appdata-" + Guid.NewGuid().ToString("N"));
        var logDir = Path.Combine(dataDir, "logs");
        try
        {
            using var fileProvider = new FileLoggerProvider(logDir);
            var factory = Microsoft.Extensions.Logging.LoggerFactory.Create(b =>
                b.AddProvider(fileProvider));
            var sp = McKuro.Services.AppServices.BuildForTesting(dataDir, factory);

            var logger = sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<McKuro.Services.GeetVerifyService>>();
            logger.LogInformation("di injected log line");

            var categoryDir = Path.Combine(logDir, "GeetVerifyService");
            Assert.True(Directory.Exists(categoryDir), "DI 注入的 logger 应写入类型目录");
            var file = Directory.GetFiles(categoryDir, "*.log").Single();
            Assert.Contains("di injected log line", ReadShared(file));
        }
        finally
        {
            try { Directory.Delete(dataDir, true); } catch { }
        }
    }
}
