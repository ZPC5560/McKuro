using System.Diagnostics;
using McKuro.Core.Services.Settings;

namespace McKuro.Core.Models.Game;

/// <summary>
/// 「自定义启动程序」(mod 加载器等)的配置与启动参数构造。
/// <para>
/// <b>与 `StartGameExeName` 的区别:</b>那是<b>游戏目录内的文件名</b>(只能指向游戏目录里的 exe);
/// 这里是<b>任意绝对路径</b>的外部程序,且带自己的一份附加参数。mod 用户通常把加载器装在
/// 游戏目录之外,两者不能合并。
/// </para>
/// <para>
/// <b>工作目录:</b>取自定义程序<b>自己所在的目录</b>(不是游戏目录)。这是 mod 加载器的常见预期,
/// 也是上游 Haiyu 1.4.6 的做法;需要游戏目录的加载器可自行解析相对路径。
/// </para>
/// </summary>
public sealed class CustomLauncherOptions
{
    /// <summary>是否启用(关闭时保留已选路径,下次开启不用重选)。</summary>
    public bool Enabled { get; init; }

    /// <summary>自定义程序绝对路径。</summary>
    public string Path { get; init; } = "";

    /// <summary>追加到游戏参数<b>之后</b>的启动器自己的参数。</summary>
    public string Arguments { get; init; } = "";

    /// <summary>从设置里读(未设置也返回实例,<see cref="Enabled"/> 为 false)。</summary>
    public static CustomLauncherOptions FromSettings(AppSettings? settings) => new()
    {
        Enabled = settings?.CustomLauncherEnabled ?? false,
        Path = settings?.CustomLauncherPath ?? "",
        Arguments = settings?.CustomLauncherArguments ?? "",
    };

    /// <summary>
    /// 校验能否使用。<b>必须在每次启动时调用</b>(不能只在保存时校验):
    /// 用户可能在保存之后把那个 exe 删了/挪了。
    /// </summary>
    /// <returns>错误文案;null = 可用。</returns>
    public string? Validate()
    {
        if (string.IsNullOrWhiteSpace(Path))
        {
            return "未选择自定义启动程序";
        }
        if (!System.IO.Path.IsPathFullyQualified(Path))
        {
            return "自定义启动程序必须是完整路径(如 D:\\ModLauncher\\loader.exe)";
        }
        if (!File.Exists(Path))
        {
            return $"自定义启动程序不存在: {Path}";
        }
        return null;
    }

    /// <summary>
    /// 构造启动参数:游戏参数在前、启动器参数追加在后(空段自动省略,不产生多余空格)。
    /// <para>
    /// <b>不设置 <c>Verb = "runas"</c>:</b>自定义程序是否需要提权应由它自身决定
    /// (其清单/兼容性设置),启动器不该替它决定以管理员身份运行。
    /// </para>
    /// </summary>
    public ProcessStartInfo CreateStartInfo(string gameArguments)
    {
        var args = string.Join(' ', new[] { gameArguments?.Trim() ?? "", Arguments?.Trim() ?? "" }
            .Where(s => s.Length > 0));

        return new ProcessStartInfo
        {
            FileName = Path,
            Arguments = args,
            WorkingDirectory = System.IO.Path.GetDirectoryName(Path) ?? "",
            UseShellExecute = true,
        };
    }

    /// <summary>该自定义程序的进程名(不含扩展名),供进程监控盯着正确的进程。</summary>
    public string? ProcessName =>
        string.IsNullOrWhiteSpace(Path) ? null : System.IO.Path.GetFileNameWithoutExtension(Path);
}
