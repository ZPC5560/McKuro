using System;
using System.Collections.Generic;
using Avalonia.Controls;

namespace McKuro.Services;

/// <summary>
/// 系统桌面材质(操作系统原生背景)。
/// 导航栏/内容背景跟随当前系统材质:Win11=Mica、Win10=Acrylic、
/// macOS 15=毛玻璃(NSVisualEffectView 染色)、macOS 26=液态玻璃(同一视图由系统自动升级渲染)、
/// Linux=透明窗口+应用自带轻量染色(各发行版合成器下近似毛玻璃,X11/Wayland 无标准模糊协议)。
/// </summary>
public enum SystemMaterialKind
{
    /// <summary>Windows 11 (10.0.22000+) 壁纸染色模糊。</summary>
    Mica,

    /// <summary>Windows 10 (1809+) Acrylic 高半径模糊。</summary>
    Acrylic,

    /// <summary>macOS NSVisualEffectView(15 Sequoia=毛玻璃;26 Tahoe 由系统渲染为液态玻璃)。</summary>
    MacVibrancy,

    /// <summary>Linux:透明窗口+应用染色(不同发行版/合成器下效果一致,近似毛玻璃)。</summary>
    LinuxTransparentTint,
}

/// <summary>
/// 系统材质探测(静态,进程生命周期内不变):
/// 提供 <see cref="TransparencyLevelHint"/> 供窗口设置透明级别提示,
/// <see cref="IsOsBackdropActive"/> 表示是否启用 OS 原生背景(Mica/Acrylic/毛玻璃)。
/// </summary>
public static class SystemMaterialService
{
    /// <summary>当前系统材质。</summary>
    public static SystemMaterialKind Kind { get; }

    /// <summary>是否采用 OS 原生背景(此时应让涂抹层透明,露出系统材质)。</summary>
    public static bool IsOsBackdropActive =>
        Kind is SystemMaterialKind.Mica or SystemMaterialKind.Acrylic or SystemMaterialKind.MacVibrancy;

    /// <summary>窗口透明级别提示(含平台回退链:如 Mica 不可用 → Acrylic → Blur)。</summary>
    public static IReadOnlyList<WindowTransparencyLevel> TransparencyLevelHint { get; }

    /// <summary>用户可读的材质名(设置页展示用)。</summary>
    public static string DisplayName => Kind switch
    {
        SystemMaterialKind.Mica => "Windows 11 Mica",
        SystemMaterialKind.Acrylic => "Windows 10 Acrylic",
        SystemMaterialKind.MacVibrancy =>
            OperatingSystem.IsMacOSVersionAtLeast(26, 0) ? "macOS 26 液态玻璃" : "macOS 毛玻璃",
        _ => "Linux 透明背景",
    };

    static SystemMaterialService()
    {
        if (OperatingSystem.IsWindows())
        {
            // Win11 22000+ = Mica;Win10 1809+ = Acrylic(两档内部均有更低模糊回退)
            Kind = OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000)
                ? SystemMaterialKind.Mica
                : SystemMaterialKind.Acrylic;
            TransparencyLevelHint = Kind == SystemMaterialKind.Mica
                ? [WindowTransparencyLevel.Mica, WindowTransparencyLevel.AcrylicBlur, WindowTransparencyLevel.Blur]
                : [WindowTransparencyLevel.AcrylicBlur, WindowTransparencyLevel.Blur];
        }
        else if (OperatingSystem.IsMacOS())
        {
            // macOS:NSVisualEffectView 毛玻璃(15)/液态玻璃(26,系统按版本自动升级材质样式)
            Kind = SystemMaterialKind.MacVibrancy;
            TransparencyLevelHint = [WindowTransparencyLevel.Blur];
        }
        else
        {
            // Linux:无标准模糊协议(合成器壁纸模糊不由应用控制),保持透明窗口+应用染色
            Kind = SystemMaterialKind.LinuxTransparentTint;
            TransparencyLevelHint = [];
        }
    }
}
