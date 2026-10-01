using System.Diagnostics;
using System.Text;

namespace McKuro.Core.Services.Infrastructure;

/// <summary>
/// 桌面快捷方式(.lnk)创建。
/// <para>
/// 实现方式说明(经实测确定):通过 <c>WScript.Shell</c> COM 生成 .lnk。
/// </para>
/// <para>
/// 为什么不自己拼 Shell Link 二进制:实测手工只写 ShellLinkHeader + LinkInfo(LocalBasePath +
/// CommonPathSuffix)产出的 .lnk,Windows 能读出 Arguments/WorkingDirectory/Description,
/// 但 <b>TargetPath 为空且双击无法启动</b> —— 因为 Windows 的资源解析依赖
/// LinkTargetIDList(HasIdList),而生成合法 IDList 需要完整的 shell 命名空间构造。
/// 因此这里改用系统自带的 WScript.Shell。
/// </para>
/// <para>
/// 为什么走 PowerShell 子进程而不是进程内 COM:本项目 Native AOT 发布时
/// <c>BuiltInComInteropSupport=false</c>(见 McKuro.csproj),进程内 COM 激活在发布版不可用;
/// 而用户自建的 PowerShell 进程能正常使用 COM。该操作是用户手动触发的一次性动作
/// (实测约 300ms),子进程开销可接受。
/// </para>
/// </summary>
public static class DesktopShortcutService
{
    /// <summary>当前平台是否支持创建 .lnk 快捷方式。</summary>
    public static bool IsSupported => OperatingSystem.IsWindows();

    /// <summary>
    /// 创建快捷方式,返回写出的 .lnk 路径;平台不支持或失败返回 null。
    /// </summary>
    /// <param name="shortcutPath">.lnk 目标路径(如 桌面\鸣潮启动器.lnk)。</param>
    /// <param name="targetPath">快捷方式指向的可执行文件绝对路径。</param>
    /// <param name="workingDir">工作目录(可空)。</param>
    /// <param name="arguments">命令行参数(可空)。</param>
    /// <param name="iconPath">图标来源(可空;为空时用目标 exe 自带图标)。</param>
    /// <param name="description">备注/显示名(可空)。</param>
    public static string? CreateShortcut(
        string shortcutPath,
        string targetPath,
        string? workingDir = null,
        string? arguments = null,
        string? iconPath = null,
        string? description = null)
    {
        if (!IsSupported
            || string.IsNullOrWhiteSpace(shortcutPath)
            || string.IsNullOrWhiteSpace(targetPath))
        {
            return null;
        }

        try
        {
            var dir = Path.GetDirectoryName(shortcutPath);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }

            // 已存在先删除:WScript.Shell.Save() 对既有文件行为不一致(可能保留旧字段)。
            if (File.Exists(shortcutPath))
            {
                File.Delete(shortcutPath);
            }

            var script = BuildScript(shortcutPath, targetPath, workingDir, arguments, iconPath, description);
            var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));

            using var proc = Process.Start(new ProcessStartInfo
            {
                FileName = "powershell.exe",
                // -NoProfile/-NonInteractive 避免加载用户配置拖慢或弹交互;
                // -EncodedCommand 不受脚本执行策略限制。
                Arguments = $"-NoProfile -NonInteractive -WindowStyle Hidden -EncodedCommand {encoded}",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true,
            });

            proc?.WaitForExit(30_000);

            // 以产物存在且非空作为成功判据(COM 调用失败时不会写出文件)
            return File.Exists(shortcutPath) && new FileInfo(shortcutPath).Length > 0
                ? shortcutPath
                : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// 生成创建快捷方式的 PowerShell 脚本。路径全部走单引号字符串并对单引号做转义
    /// (PowerShell 单引号串中 <c>''</c> 表示一个字面单引号),避免路径含 <c>'</c> 或 <c>$</c> 时被注入。
    /// </summary>
    public static string BuildScript(
        string shortcutPath,
        string targetPath,
        string? workingDir,
        string? arguments,
        string? iconPath,
        string? description)
    {
        var sb = new StringBuilder();
        sb.Append("$ErrorActionPreference='Stop';");
        // WScript.Shell 是 Windows 自带 COM;CreateShortcut 对不存在的 .lnk 会新建对象
        sb.Append("$s=(New-Object -ComObject WScript.Shell).CreateShortcut(").Append(Quote(shortcutPath)).Append(");");
        sb.Append("$s.TargetPath=").Append(Quote(targetPath)).Append(';');

        if (!string.IsNullOrWhiteSpace(workingDir))
        {
            sb.Append("$s.WorkingDirectory=").Append(Quote(workingDir!)).Append(';');
        }
        if (!string.IsNullOrWhiteSpace(arguments))
        {
            sb.Append("$s.Arguments=").Append(Quote(arguments!)).Append(';');
        }
        if (!string.IsNullOrWhiteSpace(description))
        {
            sb.Append("$s.Description=").Append(Quote(description!)).Append(';');
        }
        if (!string.IsNullOrWhiteSpace(iconPath))
        {
            sb.Append("$s.IconLocation=").Append(Quote(iconPath!)).Append(';');
        }

        sb.Append("$s.Save();");
        return sb.ToString();
    }

    /// <summary>PowerShell 单引号字符串字面量(内部的 ' 翻倍转义)。</summary>
    private static string Quote(string value) => "'" + value.Replace("'", "''") + "'";
}
