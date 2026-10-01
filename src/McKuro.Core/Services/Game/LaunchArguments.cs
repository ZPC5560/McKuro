using System.Text;

namespace McKuro.Core.Services.Game;

/// <summary>
/// 鸣潮启动资源等级(对应官方启动器「选择资源等级」的极致/高清/流畅)。
/// 映射到启动参数 <c>-krqlv</c> 的取值 uhd/hd/sd。
/// </summary>
public enum GameResourceLevel
{
    /// <summary>极致(uhd)。</summary>
    Ultra,

    /// <summary>高清(hd,官方启动器默认值)。</summary>
    High,

    /// <summary>流畅(sd)。</summary>
    Smooth,
}

/// <summary>
/// 鸣潮启动命令行组装(纯函数,便于单测)。
/// <para>
/// 鸣潮 3.x 起 <c>-krqlv</c> 是**必需**的引导参数:官方启动器固定以
/// <c>Wuthering Waves.exe ["-krqlv=hd"]</c> 启动(见其 KRGameProcess 日志),
/// 缺失该参数时游戏会直接以 "The Game has crashed and will close" 退出。
/// 资源等级决定加载哪一套资源包(uhd/hd/sd),必须与包体一致才能进入游戏。
/// </para>
/// </summary>
public static class LaunchArguments
{
    /// <summary>默认资源等级取值(官方启动器默认 -krqlv=hd)。</summary>
    public const string DefaultResourceLevel = "hd";

    /// <summary>资源等级参数名(取值以 '=' 连接,如 -krqlv=hd)。</summary>
    private const string ResourceLevelSwitch = "-krqlv";

    /// <summary>资源等级 → 参数取值(uhd/hd/sd)。</summary>
    public static string ToValue(GameResourceLevel level) => level switch
    {
        GameResourceLevel.Ultra => "uhd",
        GameResourceLevel.Smooth => "sd",
        _ => DefaultResourceLevel,
    };

    /// <summary>参数取值 → 资源等级(无法识别时回退高清)。</summary>
    public static GameResourceLevel FromValue(string? value) => Normalize(value) switch
    {
        "uhd" => GameResourceLevel.Ultra,
        "sd" => GameResourceLevel.Smooth,
        _ => GameResourceLevel.High,
    };

    /// <summary>
    /// 归一化资源等级取值:大小写不敏感,仅接受 uhd/hd/sd;
    /// 空值或无法识别时回退 <see cref="DefaultResourceLevel"/>。
    /// </summary>
    public static string Normalize(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "uhd" => "uhd",
        "sd" => "sd",
        "hd" => DefaultResourceLevel,
        _ => DefaultResourceLevel,
    };

    /// <summary>
    /// 组装游戏启动命令行(对齐官方启动器 <c>Client -dx11 -slno -krqlv=hd {自定义参数}</c>)。
    /// </summary>
    /// <param name="useDx11">是否以 DX11 启动(追加 -dx11 -slno,对齐 Haiyu WavesLauncheOption)。</param>
    /// <param name="resourceLevel">资源等级取值(空/非法 = 默认 hd)。</param>
    /// <param name="extraArguments">用户自定义附加参数(其中的 -krqlv 会被剥离,避免与资源等级设置冲突)。</param>
    public static string Build(bool useDx11, string? resourceLevel, string? extraArguments)
    {
        var sb = new StringBuilder("Client");
        if (useDx11)
        {
            sb.Append(" -dx11 -slno");
        }

        // 资源等级是必需参数,始终由设置(而非自定义参数)决定,保证单一事实来源。
        sb.Append(' ').Append(ResourceLevelSwitch).Append('=').Append(Normalize(resourceLevel));

        var extra = StripResourceLevel(extraArguments);
        if (extra.Length > 0)
        {
            sb.Append(' ').Append(extra);
        }

        return sb.ToString();
    }

    /// <summary>剥离自定义参数中的 -krqlv 项(该参数由资源等级设置统一管理)。</summary>
    public static string StripResourceLevel(string? extraArguments) =>
        StripResourceLevel(extraArguments, out _);

    /// <summary>
    /// 剥离自定义参数中的 -krqlv 项,并输出其取值(用于把用户手工填写的等级
    /// 迁移到资源等级设置里,避免升级后静默改变画质)。
    /// <para>兼容 <c>-krqlv=hd</c> 与 <c>-krqlv hd</c> 两种写法。</para>
    /// </summary>
    public static string StripResourceLevel(string? extraArguments, out string? detectedLevel)
    {
        detectedLevel = null;
        if (string.IsNullOrWhiteSpace(extraArguments))
        {
            return string.Empty;
        }

        var tokens = extraArguments.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        var kept = new List<string>(tokens.Length);

        for (int i = 0; i < tokens.Length; i++)
        {
            var token = tokens[i];
            if (!token.StartsWith(ResourceLevelSwitch, StringComparison.OrdinalIgnoreCase))
            {
                kept.Add(token);
                continue;
            }

            var rest = token[ResourceLevelSwitch.Length..];
            if (rest.Length > 0 && rest[0] == '=')
            {
                // -krqlv=hd
                if (rest.Length > 1)
                {
                    detectedLevel = Normalize(rest[1..]);
                }
                continue;
            }

            if (rest.Length == 0)
            {
                // -krqlv hd(空格分隔):吞掉紧随其后的取值项
                if (i + 1 < tokens.Length && !tokens[i + 1].StartsWith('-'))
                {
                    detectedLevel = Normalize(tokens[i + 1]);
                    i++;
                }
                continue;
            }

            // 形如 -krqlvhd 之类的未知写法:原样保留,不臆测语义
            kept.Add(token);
        }

        return string.Join(' ', kept);
    }

    /// <summary>
    /// 尽力探测游戏目录里已安装的资源包等级(uhd/hd/sd),无法判定时返回 null。
    /// <para>
    /// 官方启动器把已安装资源包记录在 <c>&lt;游戏目录&gt;\launcherDownloadConfig\</c>
    /// 下(<c>common.json</c> 恒有,另有 <c>hd.json</c>/<c>sd.json</c>/<c>uhd.json</c> 之一)。
    /// 资源等级必须与包体匹配,否则游戏会因找不到对应资源包而启动失败。
    /// </para>
    /// </summary>
    /// <param name="gameRoot">游戏安装根目录。</param>
    public static string? DetectInstalledResourceLevel(string? gameRoot)
    {
        if (string.IsNullOrWhiteSpace(gameRoot))
        {
            return null;
        }

        string dir;
        try
        {
            dir = Path.Combine(gameRoot, "launcherDownloadConfig");
            if (!Directory.Exists(dir))
            {
                return null;
            }
        }
        catch (Exception)
        {
            // 路径非法/无权限:交给调用方回退默认等级
            return null;
        }

        // 正常只装一套资源包;若同时存在多个标记,按精细度从高到低取
        foreach (var (fileName, level) in InstalledLevelMarkers)
        {
            try
            {
                if (File.Exists(Path.Combine(dir, fileName)))
                {
                    return level;
                }
            }
            catch (Exception)
            {
                // 单个标记探测失败不影响其余
            }
        }

        return null;
    }

    /// <summary>已安装资源包标记文件 → 等级取值(按精细度降序)。</summary>
    private static readonly (string FileName, string Level)[] InstalledLevelMarkers =
    [
        ("uhd.json", "uhd"),
        ("hd.json", DefaultResourceLevel),
        ("sd.json", "sd"),
    ];
}
