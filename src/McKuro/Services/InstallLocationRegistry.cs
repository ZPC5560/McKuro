using Microsoft.Win32;

namespace McKuro.Services;

/// <summary>
/// 安装位置自注册:主实例启动时把当前 exe 所在目录写入 HKCU\Software\McKuro\InstallPath。
/// <para>
/// 用途:Inno 安装器(setup.iss 的 {code:DefaultDir})据此把安装目录默认值定位到既有安装,
/// 补上 zip 便携版没有卸载注册表项、更新时被迫手动浏览选择文件夹的缺口。
/// 经安装器装过的场景由 Inno 原生 UsePreviousAppDir(HKLM/HKCU 卸载键)复用,不依赖这里。
/// </para>
/// <para>
/// HKCU 写入无需管理员;任何失败静默忽略——它只影响安装器默认值,不影响本程序功能。
/// 应用内自更新链路不经这里:SettingsViewModel 直接传 /DIR=当前目录,零歧义。
/// </para>
/// </summary>
public static class InstallLocationRegistry
{
    /// <summary>注册表子键路径(供 setup.iss 对照,勿单方面改动)。</summary>
    public const string SubKeyPath = @"Software\McKuro";
    public const string ValueName = "InstallPath";

    /// <summary>把当前安装目录注册到 HKCU(仅 Windows;失败静默)。</summary>
    public static void TryRegister()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }
        try
        {
            var dir = Path.GetDirectoryName(Environment.ProcessPath);
            if (string.IsNullOrEmpty(dir))
            {
                return;
            }
            using var key = Registry.CurrentUser.CreateSubKey(SubKeyPath);
            key?.SetValue(ValueName, dir);
        }
        catch (Exception)
        {
            // 注册表不可写(策略限制等):安装器退化为手动选目录,不影响启动
        }
    }
}
