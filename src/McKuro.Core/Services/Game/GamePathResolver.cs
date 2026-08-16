namespace McKuro.Core.Services.Game;

/// <summary>服务器/渠道类型。</summary>
public enum GameServerType
{
    /// <summary>官服(国服)</summary>
    Official,
    /// <summary>B站</summary>
    Bilibili,
    /// <summary>WeGame</summary>
    WeGame,
    /// <summary>国际服</summary>
    Global,
    Unknown,
}

/// <summary>游戏类型。</summary>
public enum KuroGame
{
    /// <summary>鸣潮。</summary>
    Waves,
    /// <summary>战双(帕弥什)。</summary>
    Punish,
}

/// <summary>
/// 解析游戏安装目录相关路径与信息(参考 WutheringWavesTool 的 GameResourcesManager)。
/// 鸣潮目录结构(Windows 端):
///   Wuthering Waves.exe
///   Client/Binaries/Win64/Client-Win64-Shipping.exe
///   Client/Saved/Logs/Client.log
///   Client/Saved/LocalStorage/LocalStorage.db
/// 战双目录结构(Windows 端):
///   PGR.exe
///   Client/Binaries/Win64/Client-Win64-Shipping.exe
/// </summary>
public sealed class GamePathResolver
{
    public const string ExeRootName = "Wuthering Waves.exe";
    public const string ExeClientRelative = "Client/Binaries/Win64/Client-Win64-Shipping.exe";
    public const string LogRelative = "Client/Saved/Logs";
    public const string ClientLogRelative = "Client/Saved/Logs/Client.log";
    public const string LocalStorageRelative = "Client/Saved/LocalStorage/LocalStorage.db";

    private readonly Func<string?> _rootDirGetter;

    /// <summary>游戏主程序文件名(战双为 PGR.exe)。</summary>
    public string GameExeName { get; }

    public GamePathResolver(Func<string?> rootDirGetter, string gameExeName = ExeRootName)
    {
        _rootDirGetter = rootDirGetter;
        GameExeName = gameExeName;
    }

    public string? GameRootDir => _rootDirGetter();

    public bool IsGameInstalled => GameRootDir is not null && File.Exists(Path.Combine(GameRootDir, GameExeName));

    public string? ClientExePath =>
        GameRootDir is null ? null : Path.Combine(GameRootDir, ExeClientRelative);

    public string? RootExePath =>
        GameRootDir is null ? null : Path.Combine(GameRootDir, GameExeName);

    public string? ClientLogPath =>
        GameRootDir is null ? null : Path.Combine(GameRootDir, ClientLogRelative);

    public string? LogDir =>
        GameRootDir is null ? null : Path.Combine(GameRootDir, LogRelative);

    public string? LocalStorageDbPath =>
        GameRootDir is null ? null : Path.Combine(GameRootDir, LocalStorageRelative);

    /// <summary>检测服务器/渠道类型(依据 SDK 目录)。</summary>
    public GameServerType DetectServerType()
    {
        if (GameRootDir is null)
        {
            return GameServerType.Unknown;
        }

        string clientBinaries = Path.Combine(GameRootDir, "Client", "Binaries", "Win64");
        if (Directory.Exists(Path.Combine(clientBinaries, "ThirdParty", "KrPcSdk_Mainland", "KRSDKRes", "Bilibili")))
        {
            return GameServerType.Bilibili;
        }
        if (Directory.Exists(Path.Combine(clientBinaries, "ThirdParty", "KrPcSdk_Mainland", "KRSDKRes", "wegame")))
        {
            return GameServerType.WeGame;
        }
        if (Directory.Exists(Path.Combine(clientBinaries, "ThirdParty", "KrPcSdk_Global")))
        {
            return GameServerType.Global;
        }
        if (Directory.Exists(Path.Combine(clientBinaries, "ThirdParty", "KrPcSdk_Mainland")))
        {
            return GameServerType.Official;
        }
        return GameServerType.Unknown;
    }

    /// <summary>计算游戏目录占用大小。</summary>
    public static long GetDirectorySize(string rootDir)
    {
        long size = 0;
        try
        {
            foreach (var file in Directory.EnumerateFiles(rootDir, "*", SearchOption.AllDirectories))
            {
                try
                {
                    size += new FileInfo(file).Length;
                }
                catch (Exception)
                {
                    // 忽略无法访问的文件
                }
            }
        }
        catch (Exception)
        {
            // 忽略目录访问错误
        }
        return size;
    }
}
