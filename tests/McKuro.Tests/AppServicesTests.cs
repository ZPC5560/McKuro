using McKuro.Core.Infrastructure;
using McKuro.Core.Services.Settings;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace McKuro.Tests;

/// <summary>
/// 验证 DI 容器 + ISettingsService 的契约。<para>
/// 为避免 tests 依赖 McKuro(WinExe),这里直接构造一个等价的最小 ServiceCollection。
/// McKuro 项目里的 <see cref="McKuro.Services.AppServices.RegisterCore"/> 使用同样的注册方式(单元测试里覆盖相同行为)。</para>
/// </summary>
public class ServiceContainerContractTests : IDisposable
{
    private readonly string _dir;

    public ServiceContainerContractTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "McKuro-di-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* ignore */ }
    }

    private static ServiceCollection BuildCore(string dataDir)
    {
        var services = new ServiceCollection();
        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddSingleton(sp => new SettingsService(
            dataDir,
            sp.GetRequiredService<ILoggerFactory>().CreateLogger<SettingsService>()));
        services.AddSingleton<ISettingsService>(sp => sp.GetRequiredService<SettingsService>());
        return services;
    }

    [Fact]
    public void Resolves_ISettingsService_From_Container()
    {
        var sp = BuildCore(_dir).BuildServiceProvider();
        var s = sp.GetRequiredService<ISettingsService>();
        Assert.NotNull(s.Current);
        Assert.Equal("zh-Hans", s.Current.Language);
    }

    [Fact]
    public void Settings_Is_Registered_As_Singleton()
    {
        var sp = BuildCore(_dir).BuildServiceProvider();
        var a = sp.GetRequiredService<ISettingsService>();
        var b = sp.GetRequiredService<ISettingsService>();
        Assert.Same(a, b);
    }

    [Fact]
    public void Two_Containers_Isolate_Settings()
    {
        var dir2 = Path.Combine(Path.GetTempPath(), "McKuro-di-" + Guid.NewGuid().ToString("N"));
        try
        {
            var sp1 = BuildCore(_dir).BuildServiceProvider();
            var sp2 = BuildCore(dir2).BuildServiceProvider();
            sp1.GetRequiredService<ISettingsService>().Current.GameRootDir = "one";
            Assert.Equal("", sp2.GetRequiredService<ISettingsService>().Current.GameRootDir);
        }
        finally
        {
            try { Directory.Delete(dir2, recursive: true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void FullContainer_Resolves_AppDatabase_And_Updater()
    {
        // 回归:AppDatabase 构造函数需要 dataDir,必须工厂注册(AddSingleton<AppDatabase>() 会因
        // 无法解析 string 而抛 "Unable to resolve service for type 'System.String'")。
        var sp = McKuro.Services.AppServices.BuildForTesting(_dir);
        var db = sp.GetRequiredService<AppDatabase>();
        Assert.NotNull(db);
        var updater = sp.GetRequiredService<McKuro.Core.Services.Game.IGameUpdater>();
        Assert.NotNull(updater);
        var store = sp.GetRequiredService<McKuro.Core.Services.Gacha.GachaRecordStore>();
        Assert.NotNull(store);
    }
}
