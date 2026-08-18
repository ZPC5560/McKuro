using System.Runtime.InteropServices;

namespace McKuro.Services;

/// <summary>
/// 描述当前桌面平台可安全提供的能力。
/// 业务页可以据此隐藏或解释平台专属操作，避免在 macOS/Linux 上显示必然失败的 Windows 动作。
/// </summary>
public sealed class PlatformCapabilities
{
    public bool IsWindows { get; } = OperatingSystem.IsWindows();
    public bool IsMacOS { get; } = OperatingSystem.IsMacOS();
    public bool IsLinux { get; } = OperatingSystem.IsLinux();

    public string PlatformName { get; }
    public string RuntimeDescription { get; } = RuntimeInformation.OSDescription;

    /// <summary>当前版本是否可以直接管理 Windows 游戏安装、修复和启动流程。</summary>
    public bool SupportsNativeGameManagement => IsWindows;

    /// <summary>当前版本是否可以读取 Windows 游戏本地缓存。</summary>
    public bool SupportsLocalGameCache => IsWindows;

    /// <summary>应用更新器是否可以直接执行现有安装器。</summary>
    public bool SupportsExecutableInstaller => IsWindows;

    public string GameSupportText => IsWindows
        ? "Windows 游戏安装、更新、修复和启动功能可用"
        : IsMacOS
            ? "macOS 保留数据与启动器功能；官方游戏由 App Store 管理"
            : IsLinux
                ? "Linux 保留数据与启动器功能；原生游戏与兼容层需用户自行配置"
                : "当前平台提供通用数据与界面功能，游戏管理能力有限";

    public string VideoSupportText => IsWindows
        ? "优先使用应用内置媒体运行库，失败时自动显示静态首帧"
        : IsMacOS
            ? "优先使用应用内媒体运行库，失败时自动显示静态首帧"
            : IsLinux
                ? "优先使用系统 libmpv，缺失时自动显示静态首帧"
                : "媒体运行库不可用时自动显示静态首帧";

    public PlatformCapabilities()
    {
        PlatformName = IsWindows
            ? "Windows"
            : IsMacOS
                ? "macOS"
                : IsLinux
                    ? "Linux"
                    : RuntimeInformation.OSDescription;
    }
}
