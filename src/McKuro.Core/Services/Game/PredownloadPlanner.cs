using McKuro.Core.Models.Game;

namespace McKuro.Core.Services.Game;

/// <summary>
/// 预下载规格的纯逻辑部分(不触网、不读盘,便于单测)。
/// <para>
/// <b>为什么必须有这一层:</b>「全量 / 精简」的体积预估与实际下载必须走同一套判据。
/// 若预估按一种规则算、下载按另一种规则算,用户会看到「说好 7.30 GB,怎么下了 12 GB」——
/// 而这正是最容易漂移的地方(两处各写一遍 if)。
/// </para>
/// <para>
/// 判据来自补丁清单顶层的 <c>tagGroups.min.exclude_prefixes</c>(3.8 起官方才有该节点)。
/// 命中的<b>分组</b>在精简规格下整组不下载:krpdiff 差分包以分组为原子边界,
/// 只剔分组内个别文件会得到一个合不出来的差分。
/// </para>
/// </summary>
public static class PredownloadPlanner
{
    /// <summary>取「精简规格要排除的路径前缀」;没有该节点时返回空数组(等价于全量)。</summary>
    public static IReadOnlyList<string> ExcludePrefixes(KuroPatchIndex? patch) =>
        (patch?.TagGroups?.Min?.ExcludePrefixes ?? [])
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(p => p.Replace('\\', '/').Trim())
            .ToArray();

    /// <summary>路径是否命中排除前缀(前缀比较区分大小写:服务端路径是固定大小写)。</summary>
    public static bool IsExcluded(string? dest, IReadOnlyList<string> prefixes) =>
        !string.IsNullOrWhiteSpace(dest)
        && prefixes.Count > 0
        && prefixes.Any(p => dest.Replace('\\', '/').StartsWith(p, StringComparison.Ordinal));

    /// <summary>
    /// 算出「整组都不要」的分组包名集合。
    /// <para>
    /// 判据:该分组 <c>dstFiles</c> 的 <b>全部</b>目标路径都命中排除前缀。
    /// 只要有一条落在排除范围之外,这个分组就必须保留 —— 它产出的是别处也要用的资源。
    /// </para>
    /// <para>
    /// <b>空 dstFiles 的分组保守保留</b>:没有判据就不猜,宁可多下一份也不制造半个差分。
    /// </para>
    /// </summary>
    public static HashSet<string> DroppedGroupDests(KuroPatchIndex? patch, IReadOnlyList<string> prefixes)
    {
        var dropped = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (patch is null || prefixes.Count == 0)
        {
            return dropped;
        }

        foreach (var group in patch.GroupInfos ?? [])
        {
            if (string.IsNullOrWhiteSpace(group?.Dest))
            {
                continue;
            }

            var dests = (group.DstFiles ?? [])
                .Where(f => !string.IsNullOrWhiteSpace(f.Dest))
                .ToArray();
            if (dests.Length == 0 || !dests.All(f => IsExcluded(f.Dest, prefixes)))
            {
                continue;
            }

            dropped.Add(group.Dest.Replace('\\', '/'));
        }

        return dropped;
    }

    /// <summary>该 resource 条目是否属于本次规格要下载的内容(分组包 / zip 包 / 普通资源统一判据)。</summary>
    public static bool IsResourceKept(KuroPatchEntry? entry, IReadOnlyCollection<string> droppedGroups, IReadOnlyList<string> prefixes)
    {
        if (entry?.Dest is null)
        {
            return false;
        }

        var dest = entry.Dest.Replace('\\', '/');
        return !droppedGroups.Contains(dest) && !IsExcluded(dest, prefixes);
    }

    /// <summary>某规格下要下载的总字节数(与 <see cref="GameManifestLoader.LoadPatchAsync"/> 的取舍逐条对齐)。</summary>
    public static long DownloadBytes(KuroPatchIndex? patch, PredownloadSpec spec)
    {
        if (patch is null)
        {
            return 0;
        }

        var prefixes = spec == PredownloadSpec.Minimal ? ExcludePrefixes(patch) : [];
        var droppedGroups = DroppedGroupDests(patch, prefixes);
        long total = 0;
        foreach (var entry in patch.Resource ?? [])
        {
            if (IsResourceKept(entry, droppedGroups, prefixes))
            {
                total += entry.Size ?? 0;
            }
        }

        return total;
    }

    /// <summary>
    /// 某规格下的最大单文件字节数。
    /// <para>
    /// 用来复现官方「所需空间」口径:实测国服 3.8.0(HD 组合)下载 12.08 GiB、
    /// 官方要求预留 13.48 GiB = 下载体积 + 最大单文件 1.40 GiB。
    /// 这与「边下边的峰值占用」吻合:最后一个大文件落位前,已下部分与它自己同时在盘上。
    /// </para>
    /// </summary>
    public static long LargestFileBytes(KuroPatchIndex? patch, PredownloadSpec spec)
    {
        if (patch is null)
        {
            return 0;
        }

        var prefixes = spec == PredownloadSpec.Minimal ? ExcludePrefixes(patch) : [];
        var droppedGroups = DroppedGroupDests(patch, prefixes);
        long largest = 0;
        foreach (var entry in patch.Resource ?? [])
        {
            if (IsResourceKept(entry, droppedGroups, prefixes))
            {
                largest = Math.Max(largest, entry.Size ?? 0);
            }
        }

        return largest;
    }
}
