using System;
using System.Diagnostics;

namespace McKuro.Services;

/// <summary>
/// 打开外部链接的唯一入口:只允许 http/https 绝对地址交给系统 shell。
/// <para>远程数据(轮播 JumpUrl、公告、Wiki 横幅)里的链接一旦被篡改,可能是 file:、UNC 路径
/// 或 ms-msdt: 之类的协议处理器;UseShellExecute 会直接执行它们,所以必须先过滤协议。</para>
/// </summary>
public static class SafeUrl
{
    /// <summary>是否为可交给浏览器打开的 http/https 绝对地址。</summary>
    public static bool IsWebUrl(string? url, out Uri? uri)
    {
        uri = null;
        if (string.IsNullOrWhiteSpace(url))
        {
            return false;
        }
        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var parsed))
        {
            return false;
        }
        if (parsed.Scheme != Uri.UriSchemeHttps && parsed.Scheme != Uri.UriSchemeHttp)
        {
            return false;
        }
        uri = parsed;
        return true;
    }

    /// <summary>是否为可交给浏览器打开的 http/https 绝对地址。</summary>
    public static bool IsWebUrl(string? url) => IsWebUrl(url, out _);

    /// <summary>
    /// 在默认浏览器中打开 http/https 链接。协议不合法或启动失败时返回 false,不抛异常。
    /// </summary>
    public static bool TryOpen(string? url)
    {
        if (!IsWebUrl(url, out var uri) || uri is null)
        {
            return false;
        }
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = uri.AbsoluteUri,
                UseShellExecute = true,
            });
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
