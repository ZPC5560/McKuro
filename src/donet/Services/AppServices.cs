using donet.Core.Infrastructure;
using donet.Core.Services.Gacha;
using donet.Core.Services.Game;
using donet.Core.Services.Roles;
using donet.Core.Services.Settings;

namespace donet.Services;

/// <summary>
/// 服务装配(手动 DI,避免反射,保证 Native AOT 兼容)。
/// </summary>
public static class AppServices
{
    private static bool _initialized;

    public static string AppDataDir { get; private set; } = "";

    public static AppDatabase Database { get; private set; } = null!;
    public static SettingsService Settings { get; private set; } = null!;
    public static HttpClient Http { get; private set; } = null!;
    public static GamePathResolver Paths { get; set; } = null!;
    public static GameManifestLoader ManifestLoader { get; private set; } = null!;
    public static DownloadEngine Downloader { get; private set; } = null!;
    public static UpdateInstaller Installer { get; private set; } = null!;
    public static GameUpdater GameUpdater { get; private set; } = null!;
    public static GachaApiClient GachaApi { get; private set; } = null!;
    public static GachaRecordStore GachaStore { get; private set; } = null!;
    public static GachaSyncService GachaSync { get; private set; } = null!;
    public static RemoteUpPoolProvider UpPools { get; private set; } = null!;
    public static KujiequApiClient KujiequApi { get; private set; } = null!;
    public static LocalRoleDataReader LocalRoles { get; private set; } = null!;
    public static RoleDataService Roles { get; private set; } = null!;

    public static void Initialize(string? overrideDataDir = null)
    {
        if (_initialized)
        {
            return;
        }

        AppDataDir = overrideDataDir
            ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "donet");

        Http = new HttpClient(new HttpClientHandler { AutomaticDecompression = System.Net.DecompressionMethods.All })
        {
            Timeout = TimeSpan.FromSeconds(60),
        };

        Settings = new SettingsService(AppDataDir);
        Database = new AppDatabase(AppDataDir);
        Paths = new GamePathResolver(() => Settings.Current.GameRootDir);

        ManifestLoader = new GameManifestLoader(Http);
        Downloader = new DownloadEngine(Http, Settings.Current.DownloadConcurrency);
        Installer = new UpdateInstaller();
        GameUpdater = new GameUpdater(ManifestLoader, Downloader, Installer, Paths, AppDataDir);

        GachaApi = new GachaApiClient(Http);
        GachaStore = new GachaRecordStore(Database);
        GachaSync = new GachaSyncService(GachaApi, GachaStore, Paths);
        UpPools = new RemoteUpPoolProvider(Http);

        KujiequApi = new KujiequApiClient(Http);
        LocalRoles = new LocalRoleDataReader(Paths);
        Roles = new RoleDataService(KujiequApi, LocalRoles, Database);

        _initialized = true;
    }
}
