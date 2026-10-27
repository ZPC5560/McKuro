using McKuro.Core.Services.Game;

namespace McKuro.Services;

/// <summary>用户选择的游戏目录属于哪种情况。</summary>
public enum GameDirVerdict
{
    /// <summary>目录里已有游戏主程序 —— 正常的"指向已装游戏"。</summary>
    ExistingInstall,

    /// <summary>空目录或不存在的目录 —— 可以作为全新安装的目标目录。</summary>
    NewInstallTarget,

    /// <summary>非空但看起来不是游戏目录 —— 拒绝,避免把游戏灌进用户的无关文件夹。</summary>
    NotGameDirectory,
}

/// <summary>
/// 「游戏安装目录」选择校验(纯函数,供设置页与单测共用)。
/// <para>
/// 需求背景:原先选目录时<b>强制</b>要求目录里已经有 <c>Wuthering Waves.exe</c>,
/// 于是全新安装无法指定目标目录 —— 用户只能先去装一次官方客户端,再用本软件接管。
/// 现在把"空目录/新目录"也视为合法目标:选好后按「下载安装」即可装到该目录。
/// </para>
/// <para>
/// 仍要拒绝"非空且不像游戏目录"的路径:那是用户误点(如整个磁盘根、桌面、文档),
/// 把几十 GB 文件灌进去既难清理也可能破坏既有内容。判定只用**顶层**条目,
/// 不递归(对上万文件的目录做递归枚举会很慢且无必要)。
/// </para>
/// </summary>
public static class GameDirValidator
{
    /// <summary>
    /// 判定该目录能否作为游戏目录。
    /// </summary>
    /// <param name="dir">用户选择的绝对路径;空串/null 视为非法目标。</param>
    public static GameDirVerdict Validate(string? dir)
    {
        if (string.IsNullOrWhiteSpace(dir))
        {
            return GameDirVerdict.NotGameDirectory;
        }

        // 已装好的游戏:主程序在,直接认
        try
        {
            if (File.Exists(Path.Combine(dir, GamePathResolver.ExeRootName)))
            {
                return GameDirVerdict.ExistingInstall;
            }
        }
        catch (Exception)
        {
            // 路径本身非法(非法字符等):交给下面按"不可用"处理
            return GameDirVerdict.NotGameDirectory;
        }

        // 目录不存在 = 全新的目标目录(安装流程会 Directory.CreateDirectory)
        if (!Directory.Exists(dir))
        {
            return GameDirVerdict.NewInstallTarget;
        }

        // 存在但为空 = 同样可作为全新安装目标
        try
        {
            return Directory.EnumerateFileSystemEntries(dir).Any()
                ? GameDirVerdict.NotGameDirectory
                : GameDirVerdict.NewInstallTarget;
        }
        catch (Exception)
        {
            // 无权限枚举:无法确认是否为空,保守拒绝(不写入配置)
            return GameDirVerdict.NotGameDirectory;
        }
    }

    /// <summary>该判定是否应被接受(供调用方一行判断)。</summary>
    public static bool IsAcceptable(GameDirVerdict verdict)
        => verdict is GameDirVerdict.ExistingInstall or GameDirVerdict.NewInstallTarget;
}
