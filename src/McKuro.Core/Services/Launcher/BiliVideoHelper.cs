using System.Net.Http;

namespace McKuro.Core.Services.Launcher;

/// <summary>
/// B 站视频链接识别与 BV 号解析(官方启动器轮播的跳转链接常为B站宣传视频)。
/// 支持两种形态:bilibili.com/video/BVxxx 页面链接、b23.tv 短链(需跟随重定向解析)。
/// </summary>
public static class BiliVideoHelper
{
    /// <summary>是否为B站视频链接(页面链接或 b23.tv 短链)。</summary>
    public static bool IsBiliVideoUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return false;
        }
        var u = url.Trim();
        return u.Contains("bilibili.com/video/", StringComparison.OrdinalIgnoreCase)
            || u.Contains("b23.tv/", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>从链接中提取 BV 号(BV + ≥10 位字母数字);b23.tv 短链不含 BV 号,返回 null。</summary>
    public static string? TryExtractBvId(string url)
    {
        var i = url.IndexOf("BV", StringComparison.Ordinal);
        while (i >= 0)
        {
            var end = i + 2;
            while (end < url.Length && char.IsLetterOrDigit(url[end]))
            {
                end++;
            }
            if (end - (i + 2) >= 10)
            {
                return url[i..end];
            }
            i = url.IndexOf("BV", i + 2, StringComparison.Ordinal);
        }
        return null;
    }

    /// <summary>
    /// 解析视频链接为 BV 号:页面链接直接提取;b23.tv 短链跟随重定向后提取。
    /// 解析失败返回 null(调用方回退浏览器打开)。
    /// </summary>
    public static async Task<string?> ResolveBvIdAsync(string url, HttpClient http, CancellationToken ct = default)
    {
        if (TryExtractBvId(url) is { } direct)
        {
            return direct;
        }
        if (!url.Contains("b23.tv/", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            using var resp = await http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            var final = resp.RequestMessage?.RequestUri?.ToString();
            return final is null ? null : TryExtractBvId(final);
        }
        catch (Exception)
        {
            return null;
        }
    }
}
