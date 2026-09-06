using System.Diagnostics;
using System.Net;
using System.Text.RegularExpressions;

namespace McKuro.Core.Services.Infrastructure;

/// <summary>
/// 系统网络代理自动检测。
/// <para>
/// .NET 的 HttpClient.DefaultProxy 在 Windows 读系统设置、Linux 读环境变量,
/// 但 macOS 上返回空代理 —— 直连 GitHub 等站点会卡死/超时。
/// 本探测器在 macOS 上解析 `scutil --proxy`(系统代理字典,含 Clash/V2Ray 等代理工具写入的值),
/// 其余平台返回 null 交给默认解析。结果进程内缓存一次。
/// </para>
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
                    _cached = ParseScutil(ReadScutilProxy());
                }
            }
            catch
            {
                // 探测失败:按无代理处理
            }
            _detected = true;
            return _cached;
        }
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
