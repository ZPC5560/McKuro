using McKuro.Core.Infrastructure;
using McKuro.Core.Models.Game;
using McKuro.Core.Services.CloudGame;
using McKuro.Core.Services.Gacha;
using McKuro.Core.Services.Game;
using McKuro.Core.Services.Guide;
using McKuro.Core.Services.Kuro;
using McKuro.Core.Services.Launcher;
using McKuro.Core.Services.Roles;
using McKuro.Core.Services.Redeem;
using McKuro.Core.Services.Settings;
using McKuro.Core.Services.Tower;
using McKuro.Core.Services.User;
using McKuro.Core.Services.Update;
using McKuro.Core.Services.Wiki;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ILogLoggerFactory = Microsoft.Extensions.Logging.LoggerFactory;

namespace McKuro.Services;

/// <summary>
/// 服务容器(基于 Microsoft.Extensions.DependencyInjection,AOT 兼容)。
/// <para>
/// 既提供静态 facade(<see cref="Settings"/> / <see cref="GameUpdater"/> 等)以兼容既有代码,
/// 也支持 <see cref="Services"/> 获取真正的 <see cref="IServiceProvider"/>,
/// 便于后续 ViewModel 通过构造注入使用。
/// </para>
/// </summary>
public static class AppServices
{
    private static bool _initialized;

    /// <summary>当前数据目录。</summary>
    public static string AppDataDir { get; private set; } = "";

    /// <summary>根 DI 容器(只能用作根解析,scope 由调用方提供)。</summary>
    public static IServiceProvider Services { get; private set; } = null!;

    /// <summary>根 <see cref="ILoggerFactory"/>。</summary>
    public static ILoggerFactory LoggerFactory { get; private set; } = null!;

    /// <summary>文件日志目录(Windows:软件目录\logs;其他平台:%AppData%\McKuro\logs;为空表示不可用)。</summary>
    public static string LogDir { get; private set; } = "";

    // ---------- 静态 facade 兼容旧调用 ----------

    public static ISettingsService Settings => Services.GetRequiredService<ISettingsService>();
    public static AppDatabase Database => Services.GetRequiredService<AppDatabase>();
    public static HttpClient Http => Services.GetRequiredService<HttpClient>();
    public static GamePathResolver Paths => Services.GetRequiredService<GamePathResolver>();
    public static GameManifestLoader ManifestLoader => Services.GetRequiredService<GameManifestLoader>();
    public static DownloadEngine Downloader => Services.GetRequiredService<DownloadEngine>();
    public static UpdateInstaller Installer => Services.GetRequiredService<UpdateInstaller>();
    public static IGameUpdater GameUpdater => Services.GetRequiredService<IGameUpdater>();
    public static GachaApiClient GachaApi => Services.GetRequiredService<GachaApiClient>();
    public static GachaRecordStore GachaStore => Services.GetRequiredService<GachaRecordStore>();
    public static GachaAnalysisService GachaAnalysis => Services.GetRequiredService<GachaAnalysisService>();
    public static IGachaSyncService GachaSync => Services.GetRequiredService<IGachaSyncService>();
    public static CloudGachaService CloudGacha => Services.GetRequiredService<CloudGachaService>();
    public static IUpPoolProvider UpPools => Services.GetRequiredService<IUpPoolProvider>();
    public static KujiequApiClient KujiequApi => Services.GetRequiredService<KujiequApiClient>();
    public static LocalRoleDataReader LocalRoles => Services.GetRequiredService<LocalRoleDataReader>();
    public static IRoleDataService Roles => Services.GetRequiredService<IRoleDataService>();
    public static LauncherInfoService LauncherInfo => Services.GetRequiredService<LauncherInfoService>();
    public static PlayTimeService PlayTime => Services.GetRequiredService<PlayTimeService>();
    public static TowerService Tower => Services.GetRequiredService<TowerService>();
    public static DailyDataService DailyData => Services.GetRequiredService<DailyDataService>();
    public static LocalGameDailyDataService LocalDaily => Services.GetRequiredService<LocalGameDailyDataService>();
    public static IKuroClient Kuro => Services.GetRequiredService<IKuroClient>();
    public static KuroAccountService KuroAccounts => Services.GetRequiredService<KuroAccountService>();
    public static KuroSignService KuroSign => Services.GetRequiredService<KuroSignService>();
    public static CloudGameService CloudGame => Services.GetRequiredService<CloudGameService>();
    public static GuideAchievementService Guide => Services.GetRequiredService<GuideAchievementService>();
    public static WikiClient Wiki => Services.GetRequiredService<WikiClient>();
    public static RedemptionCodeService RedeemCodes => Services.GetRequiredService<RedemptionCodeService>();
    public static GeetVerifyService GeetVerify => Services.GetRequiredService<GeetVerifyService>();
    public static AppUpdateService AppUpdate => Services.GetRequiredService<AppUpdateService>();
    public static IconDiskCacheService IconCache => Services.GetRequiredService<IconDiskCacheService>();
    public static PlatformCapabilities Capabilities => Services.GetRequiredService<PlatformCapabilities>();

    /// <summary>
    /// 稳定的设备 ID(持久化到 device-id.txt,跨启动不变)。
    /// 库街区 did 头需用稳定设备码,否则每次启动新 did + 持久 token 会触发极验风控。
    /// </summary>
    public static string StableDeviceId { get; } = LoadOrCreateDeviceId(
        AppDataDir ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "McKuro"));

    /// <summary>
    /// 跨平台打开文件管理器定位路径(Windows 用默认/explorer,macOS 用 open,Linux 用 xdg-open)。
    /// 返回 false 表示打开失败。
    /// </summary>
    public static bool OpenInFileManager(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }
        try
        {
            if (OperatingSystem.IsWindows())
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = path,
                    UseShellExecute = true,
                });
            }
            else if (OperatingSystem.IsMacOS())
            {
                System.Diagnostics.Process.Start("open", $"\"{path}\"");
            }
            else
            {
                System.Diagnostics.Process.Start("xdg-open", $"\"{path}\"");
            }
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>
    /// 初始化 DI 容器(只会跑一次)。
    /// </summary>
    public static void Initialize(string? overrideDataDir = null, ILoggerFactory? loggerFactory = null)
    {
        if (_initialized)
        {
            return;
        }

        AppDataDir = overrideDataDir
            ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "McKuro");

        // McKuro 启动器自带 debug + simple console + 文件日志 三个 provider,Core 用 null factory 防止 AOT 反射回退
        // Windows 下日志写到软件(exe)所在目录的 logs\,便于随程序一起查找;其他平台仍用 %AppData%\McKuro\logs
        LogDir = OperatingSystem.IsWindows()
            ? Path.Combine(AppContext.BaseDirectory, "logs")
            : Path.Combine(AppDataDir, "logs");
        LoggerFactory = loggerFactory ?? ILogLoggerFactory.Create(builder =>
        {
            builder
                .AddDebug()
                .AddSimpleConsole(o =>
                {
                    o.SingleLine = true;
                    o.TimestampFormat = "HH:mm:ss ";
                })
                .AddProvider(new FileLoggerProvider(LogDir))
                .SetMinimumLevel(LogLevel.Information);
        });

        var services = new ServiceCollection();
        // 传入真实 LoggerFactory:否则容器内 ILoggerFactory 默认为 NullLogger,
        // 所有 DI 注入的 ILogger<T>(如 GeetVerifyService)不会写任何日志
        RegisterCore(services, AppDataDir, LoggerFactory);
        Services = services.BuildServiceProvider();
        _initialized = true;
    }

    /// <summary>供测试或工具项目以自定义数据目录构建完整容器。</summary>
    public static IServiceProvider BuildForTesting(string dataDir, ILoggerFactory? loggerFactory = null)
    {
        var services = new ServiceCollection();
        RegisterCore(services, dataDir, loggerFactory ?? NullLoggerFactory.Instance);
        return services.BuildServiceProvider();
    }

    private static void RegisterCore(IServiceCollection services, string dataDir, ILoggerFactory? loggerFactory = null)
    {
        var lf = loggerFactory ?? NullLoggerFactory.Instance;

        services.AddSingleton(lf);
        services.AddSingleton(typeof(ILogger<>), typeof(LoggerFactoryLogger<>));

        // ---- 基础 ----
        services.AddSingleton(new HttpClient(new HttpClientHandler
        {
            AutomaticDecompression = System.Net.DecompressionMethods.All,
        })
        {
            Timeout = TimeSpan.FromSeconds(60),
        });
        services.AddSingleton(sp => new SettingsService(dataDir,
            sp.GetRequiredService<ILoggerFactory>().CreateLogger<SettingsService>()));
        services.AddSingleton<ISettingsService>(sp => sp.GetRequiredService<SettingsService>());
        services.AddSingleton<PlatformCapabilities>();
        // AppDatabase 构造函数需要 dataDir(不可由容器解析),必须工厂注册
        services.AddSingleton(sp => new AppDatabase(dataDir));
        services.AddSingleton(sp => new GamePathResolver(
            () => sp.GetRequiredService<ISettingsService>().Current.GameRootDir));
        services.AddSingleton<PlayTimeService>(sp => new PlayTimeService(
            sp.GetRequiredService<GamePathResolver>(),
            sp.GetRequiredService<AppDatabase>(),
            logger: sp.GetRequiredService<ILoggerFactory>().CreateLogger<PlayTimeService>()));
        services.AddSingleton<TowerService>(sp => new TowerService(
            sp.GetRequiredService<KujiequApiClient>(),
            sp.GetRequiredService<IKuroClient>(),
            sp.GetRequiredService<KuroAccountService>(),
            logger: sp.GetRequiredService<ILoggerFactory>().CreateLogger<TowerService>()));
        services.AddSingleton<DailyDataService>(sp => new DailyDataService(
            sp.GetRequiredService<KujiequApiClient>(),
            sp.GetRequiredService<IKuroClient>(),
            sp.GetRequiredService<KuroAccountService>(),
            logger: sp.GetRequiredService<ILoggerFactory>().CreateLogger<DailyDataService>()));
        services.AddSingleton<LocalGameDailyDataService>(sp => new LocalGameDailyDataService(
            sp.GetRequiredService<HttpClient>(),
            logger: sp.GetRequiredService<ILoggerFactory>().CreateLogger<LocalGameDailyDataService>()));

        // ---- 游戏启动器(鸣潮) ----
        services.AddSingleton<GameManifestLoader>();
        services.AddSingleton<DownloadEngine>(sp => new DownloadEngine(
            sp.GetRequiredService<HttpClient>(),
            8,
            sp.GetRequiredService<ILoggerFactory>().CreateLogger<DownloadEngine>()));
        services.AddSingleton<UpdateInstaller>();
        services.AddSingleton<PatchInstaller>();
        services.AddSingleton<IGameUpdater>(sp => new GameUpdater(
            sp.GetRequiredService<GameManifestLoader>(),
            sp.GetRequiredService<DownloadEngine>(),
            sp.GetRequiredService<UpdateInstaller>(),
             sp.GetRequiredService<PatchInstaller>(),
            sp.GetRequiredService<GamePathResolver>(),
            dataDir,
            sp.GetRequiredService<AppDatabase>(),
            settings: sp.GetRequiredService<ISettingsService>(),
            logger: sp.GetRequiredService<ILoggerFactory>().CreateLogger<GameUpdater>()));

        // ---- 抽卡 ----
        services.AddSingleton<GachaApiClient>(sp => new GachaApiClient(
            sp.GetRequiredService<HttpClient>(),
            logger: sp.GetRequiredService<ILoggerFactory>().CreateLogger<GachaApiClient>()));
        services.AddSingleton<GachaRecordStore>();
        services.AddSingleton<IGachaSyncService, GachaSyncService>();
        services.AddSingleton<CloudGachaService>(sp => new CloudGachaService(
            sp.GetRequiredService<CloudGameService>(),
            sp.GetRequiredService<IGachaSyncService>(),
            sp.GetRequiredService<ISettingsService>(),
            logger: sp.GetRequiredService<ILoggerFactory>().CreateLogger<CloudGachaService>()));
        services.AddSingleton<GachaAnalysisService>();
        services.AddSingleton<IUpPoolProvider>(sp => new RemoteUpPoolProvider(
            sp.GetRequiredService<HttpClient>(),
            logger: sp.GetRequiredService<ILoggerFactory>().CreateLogger<RemoteUpPoolProvider>()));

        // ---- 库街区 ----
        services.AddSingleton<KujiequApiClient>();
        services.AddSingleton<LocalRoleDataReader>();
        services.AddSingleton<IRoleDataService>(sp => new RoleDataService(
            sp.GetRequiredService<KujiequApiClient>(),
            sp.GetRequiredService<LocalRoleDataReader>(),
            sp.GetRequiredService<AppDatabase>(),
            (KuroClient)sp.GetRequiredService<IKuroClient>(),
            sp.GetRequiredService<KuroAccountService>(),
            sp.GetRequiredService<ILoggerFactory>().CreateLogger<RoleDataService>()));
        services.AddSingleton<LauncherInfoService>();
        services.AddSingleton<IKuroClient, KuroClient>();
        services.AddSingleton<KuroAccountService>();
        services.AddSingleton<KuroSignService>(sp => new KuroSignService(
            (KuroClient)sp.GetRequiredService<IKuroClient>(),
            sp.GetRequiredService<ILoggerFactory>().CreateLogger<KuroSignService>()));

        // ---- 云游戏 / 图鉴 / 极验 ----
        services.AddSingleton(sp =>
        {
            var deviceId = LoadOrCreateDeviceId(dataDir);
            return new CloudGameService(sp.GetRequiredService<HttpClient>(), deviceId);
        });
        services.AddSingleton<GuideApiClient>(sp => new GuideApiClient(
            sp.GetRequiredService<HttpClient>(),
            logger: sp.GetRequiredService<ILoggerFactory>().CreateLogger<GuideApiClient>()));
        services.AddSingleton<GuideAchievementService>(sp => new GuideAchievementService(
            sp.GetRequiredService<CloudGameService>(),
            sp.GetRequiredService<GuideApiClient>(),
            sp.GetRequiredService<ISettingsService>(),
            logger: sp.GetRequiredService<ILoggerFactory>().CreateLogger<GuideAchievementService>()));
        services.AddSingleton<WikiClient>();
        services.AddSingleton<RedemptionCodeService>(sp => new RedemptionCodeService(
            http: null,
            logger: sp.GetRequiredService<ILoggerFactory>().CreateLogger<RedemptionCodeService>()));
        services.AddSingleton<GeetVerifyService>();
        services.AddSingleton<AppUpdateService>();

        // ---- 角色图标磁盘持久化缓存(库街区正常时缓存, mcguide 兜底时按名称复用) ----
        services.AddSingleton(sp => new IconDiskCacheService(cacheDir: Path.Combine(dataDir, "icon_cache")));
    }

    /// <summary>读取或生成稳定的设备 ID(云游戏 SDK 用)。</summary>
    private static string LoadOrCreateDeviceId(string appDataDir)
    {
        var path = Path.Combine(appDataDir, "device-id.txt");
        try
        {
            if (File.Exists(path))
            {
                var existing = File.ReadAllText(path).Trim();
                if (!string.IsNullOrEmpty(existing))
                {
                    return existing;
                }
            }
            var id = Guid.NewGuid().ToString("N");
            File.WriteAllText(path, id);
            return id;
        }
        catch (Exception)
        {
            return Guid.NewGuid().ToString("N");
        }
    }

    /// <summary>桥接 <see cref="ILoggerFactory"/> 到 <see cref="ILogger{T}"/> 的通用实现,允许通过 <c>sp.GetRequiredService&lt;ILogger&lt;Foo&gt;&gt;()</c> 解析而不必显式注册每个 T。</summary>
    private sealed class LoggerFactoryLogger<T> : ILogger<T>
    {
        private readonly ILogger _logger;
        public LoggerFactoryLogger(ILoggerFactory factory)
            => _logger = factory.CreateLogger(typeof(T).FullName ?? typeof(T).Name);
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull
            => _logger.BeginScope(state);
        public bool IsEnabled(LogLevel logLevel) => _logger.IsEnabled(logLevel);
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => _logger.Log(logLevel, eventId, state, exception, formatter);
    }
}
