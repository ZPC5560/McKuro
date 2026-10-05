using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace McKuro.Core.Services.Update;

/// <summary>
/// 更新包完整性校验(参考 Haiyu 的下载链路并补强其缺失的一环)。
/// <para>
/// Haiyu 与 McKuro 原实现都只做"下载完成即安装",不校验字节。AOT 自更新替换的是整个安装目录,
/// 一次代理劫持/截断/损坏就会把坏文件铺满安装目录,因此这里解析 Release 正文中的 sha256
/// 与资产旁的 <c>*.sha256</c> 清单,下载后比对;发布方未提供摘要时**不阻断**更新(仅记录),
/// 避免把可选能力变成硬依赖。
/// </para>
/// </summary>
public static partial class UpdateChecksum
{
    /// <summary>
    /// 从 Release 正文/清单文本中解析指定资产的 sha256。
    /// 支持常见写法:<c>&lt;64位hex&gt;  McKuro-win-x64-1.2.5.zip</c>(sha256sum 格式)、
    /// <c>&lt;hex&gt; *name</c>、<c>name: &lt;hex&gt;</c>、以及 <c>SHA256(name) = hex</c>(GitHub 自动生成的
    /// <c>*.sha256</c> 内容形态,见 <see cref="Regexes"/>)。大小写不敏感。
    /// </summary>
    public static string? ParseSha256(string? text, string assetName)
    {
        if (string.IsNullOrWhiteSpace(text) || string.IsNullOrWhiteSpace(assetName))
        {
            return null;
        }
        var name = Path.GetFileName(assetName.Trim());
        foreach (Match m in Sha256Line().Matches(text))
        {
            var listed = m.Groups["name"].Value.Trim().TrimStart('*');
            if (string.Equals(Path.GetFileName(listed), name, StringComparison.OrdinalIgnoreCase))
            {
                return m.Groups["hash"].Value.ToLowerInvariant();
            }
        }
        foreach (Match m in Sha256Function().Matches(text))
        {
            var listed = m.Groups["name"].Value.Trim();
            if (string.Equals(Path.GetFileName(listed), name, StringComparison.OrdinalIgnoreCase))
            {
                return m.Groups["hash"].Value.ToLowerInvariant();
            }
        }
        foreach (Match m in NameThenHash().Matches(text))
        {
            var listed = m.Groups["name"].Value.Trim().TrimStart('*');
            if (string.Equals(Path.GetFileName(listed), name, StringComparison.OrdinalIgnoreCase))
            {
                return m.Groups["hash"].Value.ToLowerInvariant();
            }
        }
        return null;
    }

    /// <summary>
    /// 从 <c>*.sha256</c> 清单文本解析**唯一**摘要(清单通常只含一个文件)。
    /// 出现多个不同摘要时返回 null(不猜测,交由调用方跳过校验)。
    /// </summary>
    public static string? ParseSingleSha256(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }
        var hashes = Sha256Line().Matches(text).Select(m => m.Groups["hash"].Value.ToLowerInvariant())
            .Concat(Sha256Function().Matches(text).Select(m => m.Groups["hash"].Value.ToLowerInvariant()))
            .Concat(NameThenHash().Matches(text).Select(m => m.Groups["hash"].Value.ToLowerInvariant()))
            .Distinct(StringComparer.Ordinal)
            .ToList();
        return hashes.Count == 1 ? hashes[0] : null;
    }

    /// <summary>
    /// 解析 GitHub 资产自带的摘要串(<c>sha256:&lt;64hex&gt;</c>,GitHub API 的 <c>assets[].digest</c>
    /// 与 Release 页资产行的 <c>sha256:…</c> 文本同一形态)。
    /// <para>
    /// 非 sha256 算法(如 GitHub 后续引入其他算法)返回 null,交由调用方视为"无摘要"跳过校验。
    /// </para>
    /// </summary>
    public static string? ParseDigest(string? digest)
    {
        if (string.IsNullOrWhiteSpace(digest))
        {
            return null;
        }
        var m = DigestPattern().Match(digest);
        return m.Success ? m.Groups["hash"].Value.ToLowerInvariant() : null;
    }

    /// <summary>计算文件 sha256(小写 hex);失败返回 null。</summary>
    public static async Task<string?> ComputeSha256Async(string path, CancellationToken ct = default)
    {
        try
        {
            await using var stream = new FileStream(
                path, FileMode.Open, FileAccess.Read, FileShare.Read, 128 * 1024, useAsync: true);
            var hash = await SHA256.HashDataAsync(stream, ct).ConfigureAwait(false);
            return Convert.ToHexStringLower(hash);
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>大小写不敏感比对摘要;期望值为空视为"无需校验"返回 true。</summary>
    public static bool Matches(string? expected, string? actual) =>
        string.IsNullOrWhiteSpace(expected)
        || string.Equals(expected.Trim(), actual?.Trim(), StringComparison.OrdinalIgnoreCase);

    // sha256sum 风格:"<64hex>  [*]name"(两个及以上空白分隔)
    [GeneratedRegex(
        @"(?<hash>\b[0-9a-fA-F]{64}\b)[ \t]+(?<name>\*?[^\r\n]+?)[ \t]*$",
        RegexOptions.Multiline)]
    private static partial Regex Sha256Line();

    // GitHub 自动生成摘要风格:"SHA256 (name) = <64hex>"
    [GeneratedRegex(
        @"SHA256\s*\((?<name>[^)]+)\)\s*=\s*(?<hash>[0-9a-fA-F]{64})",
        RegexOptions.IgnoreCase)]
    private static partial Regex Sha256Function();

    // "name: <64hex>" 或 "name = <64hex>"
    [GeneratedRegex(
        @"(?<name>[^\s:=][^\r\n:=]*?)\s*[:=]\s*(?<hash>\b[0-9a-fA-F]{64}\b)")]
    private static partial Regex NameThenHash();

    // GitHub 资产摘要:"sha256:<64hex>"(API assets[].digest 与 Release 页同一形态)。
    // 用 \b 锁前缀避免误匹配 "md5:…"/"sha512:…" 中的 64 位片段。
    [GeneratedRegex(
        @"(?:^|\b)sha256\s*:\s*(?<hash>[0-9a-fA-F]{64})\b",
        RegexOptions.IgnoreCase)]
    private static partial Regex DigestPattern();
}
