using McKuro.Core.Infrastructure;

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
///   DiffData/&lt;版本&gt;/           预下载暂存(启动器写入,安装时消费后删除)
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

    /// <summary>预下载暂存目录名(位于游戏目录下,不占用系统盘)。</summary>
    public const string DiffDataDirName = "DiffData";

    /// <summary>补丁包临时下载目录名(位于 DiffData 下,补丁装完即删)。</summary>
    public const string InstallTempDirName = "install_tmp";

    /// <summary>分组差分合成的临时目录名(位于游戏目录下,安装结束后删除)。</summary>
    public const string PatchTempDirName = ".McKuro_patch";

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

    /// <summary>
    /// 预下载暂存根目录(游戏目录下 DiffData)。游戏目录未设置时为 null,
    /// 调用方(如预下载)应据此报"未设置游戏目录"而不是回退到数据目录 —
    /// 预载包必须与游戏同盘,安装时才能用同卷改名而不是跨卷复制。
    /// </summary>
    public string? DiffDataDir =>
        GameRootDir is null ? null : Path.Combine(GameRootDir, DiffDataDirName);

    /// <summary>
    /// 指定版本的预下载暂存目录(游戏目录下 DiffData/&lt;版本&gt;)。
    /// <para>
    /// <b>版本号来自服务端 index.json,必须先校验再拼路径</b>:暂存目录之后可能被递归删除,
    /// <c>version = ".."</c> 会让暂存目录变成游戏根目录(根目录下没有 predownload.json → 整个游戏被删),
    /// 绝对路径则会删到任意位置。这里只接受 <see cref="IsSafeVersionSegment"/> 认可的纯数字版本号,
    /// 其余一律返回 null(调用方按"不可用"处理)。
    /// </para>
    /// </summary>
    public string? PredownloadStagingDir(string version) =>
        DiffDataDir is null || !IsSafeVersionSegment(version)
            ? null
            : Path.Combine(DiffDataDir, version);

    /// <summary>
    /// 版本号能否安全地用作 DiffData 下的单级目录名:1~4 段、每段 1~9 位 ASCII 数字、以 '.' 分隔
    /// (与 GameUpdater.TryParseVersion 认可的形态一致,但更严:不接受空白、正负号等 int.TryParse 会放过的写法)。
    /// </summary>
    public static bool IsSafeVersionSegment(string? version)
    {
        if (string.IsNullOrEmpty(version) || version.Length > 64)
        {
            return false;
        }
        var segments = version.Split('.');
        if (segments.Length is < 1 or > 4)
        {
            return false;
        }
        foreach (var segment in segments)
        {
            if (segment.Length is 0 or > 9)
            {
                return false;
            }
            foreach (var c in segment)
            {
                if (!char.IsAsciiDigit(c))
                {
                    return false;
                }
            }
        }
        return true;
    }

    /// <summary>
    /// <paramref name="path"/> 是否恰好是 <paramref name="parentDir"/> 的<b>直接</b>子目录
    /// (不是它本身、不是更深的后代、也不在它外面)。递归删除暂存/临时目录前用它做最后一道断言。
    /// </summary>
    public static bool IsDirectChildOf(string? parentDir, string? path)
    {
        if (string.IsNullOrWhiteSpace(parentDir) || string.IsNullOrWhiteSpace(path))
        {
            return false;
        }
        try
        {
            var fullParent = Path.GetFullPath(parentDir).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var fullPath = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var actualParent = Path.GetDirectoryName(fullPath);
            return !string.IsNullOrEmpty(actualParent)
                && string.Equals(actualParent, fullParent, ArchivePathGuard.PathComparison)
                && !string.IsNullOrEmpty(Path.GetFileName(fullPath));
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>
    /// 补丁包临时下载目录(DiffData/install_tmp)。与游戏同盘,补丁装完即删;
    /// 游戏目录未设置时为 null(调用方应报"未设置游戏目录",不要回退到数据目录)。
    /// </summary>
    public string? InstallTempDir =>
        DiffDataDir is null ? null : Path.Combine(DiffDataDir, InstallTempDirName);

    /// <summary>
    /// 一次性临时目录(DiffData/install_tmp/&lt;guid&gt;)。
    /// <para>
    /// 官方补丁下载<b>不再用它</b>:每次一个新 GUID 意味着补丁永远无法续传、取消还会留下孤立目录。
    /// 补丁请改用 <see cref="PatchInstallTempDir"/>(按补丁固定命名)。保留本方法供其它一次性用途。
    /// </para>
    /// </summary>
    public string? NewInstallTempDir() =>
        InstallTempDir is null ? null : Path.Combine(InstallTempDir, Guid.NewGuid().ToString("N"));

    /// <summary>补丁临时目录名前缀(DiffData/install_tmp/p-&lt;key&gt;),便于残留扫描识别"按补丁固定命名"的目录。</summary>
    public const string PatchInstallTempPrefix = "p-";

    /// <summary>
    /// 某个官方补丁专用的<b>固定</b>临时目录(DiffData/install_tmp/p-&lt;key&gt;)。
    /// <para>
    /// 同一补丁(同 indexFile + indexFileMd5 + 源版本)每次得到同一目录,下载中断/取消后
    /// 下次安装能复用已下完的文件与 .part 续传;不同补丁得到不同目录,旧补丁的残留由调用方/残留扫描清理。
    /// </para>
    /// </summary>
    /// <param name="patchKey"><see cref="ComputePatchKey"/> 的结果(只含小写十六进制)。</param>
    public string? PatchInstallTempDir(string patchKey)
    {
        if (InstallTempDir is null || string.IsNullOrEmpty(patchKey) || !patchKey.All(char.IsAsciiHexDigitLower))
        {
            return null;
        }
        return Path.Combine(InstallTempDir, PatchInstallTempPrefix + patchKey);
    }

    /// <summary>
    /// 补丁标识:对 indexFile 相对路径、indexFileMd5 与本地源版本取 SHA-256 前 8 字节(16 位小写十六进制)。
    /// 不含 CDN 前缀 —— 同一补丁换 CDN 也应复用同一目录。
    /// </summary>
    public static string ComputePatchKey(string? indexFile, string? indexFileMd5, string? sourceVersion)
    {
        var text = string.Join('\n', indexFile?.Trim() ?? "", indexFileMd5?.Trim().ToLowerInvariant() ?? "", sourceVersion?.Trim() ?? "");
        var hash = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(text));
        return Convert.ToHexString(hash, 0, 8).ToLowerInvariant();
    }

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
