using System.Diagnostics;
using System.Net;
using System.Text.RegularExpressions;

namespace McKuro.Core.Services.Infrastructure;

/// <summary>
/// 系统网络代理自动检测。
/// <para>
/// .NET 的 HttpClient.DefaultProxy 在 Windows 读系统设置(WinINET 注册表,含手动代理)、
/// Linux 读环境变量,但 macOS 上返回空代理 —— 直连 GitHub 等站点会卡死/超时。
/// 本探测器在 macOS 上解析 `scutil --proxy`(系统代理字典,含 Clash/V2Ray 等代理工具写入的值),
/// 在 Windows 上向 DefaultProxy 探测 github 的代理解析结果(命中即显式注入,行为与默认一致但可诊断),
/// Linux 交给默认解析(环境变量原生支持)。结果进程内缓存一次。
/// </para>
/// <para>注意:各平台的 PAC/WPAD 自动配置脚本模式 .NET 均不支持,代理工具请使用手动代理或 TUN 模式。</para>
/// </summary>
public static class SystemProxyDetector
{
    private static readonly object Gate = new();
    private static IWebProxy? _cached;
    private static bool _detected;

    /// <summary>探测系统代理;未启用/未检测到返回 null(调用方保留 HttpClient 默认解析)。</summary>
    public static IWebProxy? Detect()
    {
        lock (Gate)
        {
            if (_detected)
            {
                return _cached;
            }
            try
            {
                if (OperatingSystem.IsMacOS())
                {
                    // macOS:DefaultProxy 不读系统代理,scutil 解析为主,DefaultProxy 探测兜底
                    _cached = ParseScutil(ReadScutilProxy()) ?? DetectViaDefaultProxy();
                }
                else if (OperatingSystem.IsWindows())
                {
                    // Windows:DefaultProxy 原生读 WinINET 注册表,探测命中即显式注入
                    _cached = DetectViaDefaultProxy();
                }
                // Linux:环境变量由 DefaultProxy 原生处理,保持默认
            }
            catch
            {
                // 探测失败:按无代理处理
            }
            _detected = true;
            return _cached;
        }
    }

    /// <summary>
    /// 向平台默认代理解析器询问"https://github.com 该走什么代理":
    /// 返回非目标地址即存在系统代理 → 显式返回 DefaultProxy 本身(与默认行为完全一致,含绕过列表);
    /// 无代理时 IWebProxy 约定返回目标地址自身。
    /// </summary>
    private static IWebProxy? DetectViaDefaultProxy()
    {
        var target = new Uri("https://github.com");
        var via = HttpClient.DefaultProxy.GetProxy(target);
        return via is null || via.Equals(target) ? null : HttpClient.DefaultProxy;
    }

    /// <summary>读取 macOS 系统代理字典(scutil --proxy;路径固定 /usr/sbin/scutil)。</summary>
    private static string ReadScutilProxy()
    {
        using var proc = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "/usr/sbin/scutil",
                Arguments = "--proxy",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            },
        };
        proc.Start();
        var output = proc.StandardOutput.ReadToEnd();
        proc.WaitForExit(3000);
        return output;
    }

    /// <summary>解析 scutil --proxy 输出:优先 HTTPS 代理,回退 HTTP,最后 SOCKS5;
    /// 全部未启用返回 null。</summary>
    public static IWebProxy? ParseScutil(string? output)
    {
        if (string.IsNullOrWhiteSpace(output))
        {
            return null;
        }

        string? Value(string key)
        {
            var m = Regex.Match(output, $@"{key}\s*:\s*([^\r\n]+)");
            return m.Success ? m.Groups[1].Value.Trim() : null;
        }

        // HTTPS 优先(应用流量几乎全是 https),其次 HTTP,最后 SOCKS5
        if (Value("HTTPSEnable") == "1" && Value("HTTPSProxy") is { } httpsHost
            && int.TryParse(Value("HTTPSPort"), out var httpsPort) && httpsPort > 0)
        {
            return new WebProxy($"http://{httpsHost}:{httpsPort}");
        }
        if (Value("HTTPEnable") == "1" && Value("HTTPProxy") is { } httpHost
            && int.TryParse(Value("HTTPPort"), out var httpPort) && httpPort > 0)
        {
            return new WebProxy($"http://{httpHost}:{httpPort}");
        }
        if (Value("SOCKSEnable") == "1" && Value("SOCKSProxy") is { } socksHost
            && int.TryParse(Value("SOCKSPort"), out var socksPort) && socksPort > 0)
        {
            return new WebProxy($"socks5://{socksHost}:{socksPort}");
        }
        return null;
    }
}
