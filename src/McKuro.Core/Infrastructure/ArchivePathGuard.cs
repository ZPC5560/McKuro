namespace McKuro.Core.Infrastructure;

/// <summary>
/// 归档/清单类「相对路径必须落在目标目录内」的判定与安全组合(纯函数,便于单测)。
///
/// <para>
/// <b>为什么单独抽出来</b>:项目里有两处需要这条判定 —— 游戏文件清单路径
/// (<see cref="Services.Game.GameFilePath"/>)与更新包 zip 解压(App 层 SettingsViewModel)。
/// 此前 zip 解压处自己写了一个 <c>target.StartsWith(root, Ordinal)</c>,<b>少了末尾分隔符</b>,
/// 于是兄弟目录可以绕过:root=<c>C:\x\app</c>、条目 <c>../app2/evil.dll</c> 归一化后是
/// <c>C:\x\app2\evil.dll</c>,字符串前缀与 root 相同却已在目录外(评审发现)。
/// </para>
/// <para>
/// <b>正确做法</b>:两侧都归一化为绝对路径,比较时给 root 补上末尾目录分隔符,
/// 并按平台选择大小写敏感性(Windows 不敏感,误用 Ordinal 会把大小写不同的合法条目拒掉)。
/// </para>
/// </summary>
public static class ArchivePathGuard
{
    /// <summary>平台相关的路径比较方式(Windows 大小写不敏感)。</summary>
    public static StringComparison PathComparison =>
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    /// <summary>把 root 规范成「带末尾目录分隔符」的前缀,用于 <see cref="IsUnderRoot"/>。</summary>
    public static string PrefixOf(string root)
        => Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
           + Path.DirectorySeparatorChar;

    /// <summary>
    /// 判断 <paramref name="fullTarget"/>(应已 <see cref="Path.GetFullPath(string)"/> 归一化)
    /// 是否严格位于 <paramref name="root"/> 目录内。
    /// </summary>
    /// <param name="root">目标目录(可为相对路径,内部会归一化)。</param>
    /// <param name="fullTarget">归一化后的绝对目标路径。</param>
    public static bool IsUnderRoot(string root, string fullTarget)
        => fullTarget.StartsWith(PrefixOf(root), PathComparison);

    /// <summary>
    /// 组合 root 与相对路径,越界时抛 <see cref="InvalidDataException"/>。
    /// </summary>
    /// <param name="root">目标目录。</param>
    /// <param name="relative">相对路径(允许 / 与 \ 混用)。</param>
    /// <exception cref="InvalidDataException">路径越界(含 ../ 逃逸或绝对路径)。</exception>
    public static string CombineUnderRoot(string root, string relative)
    {
        if (!IsSafeRelativePath(relative))
        {
            throw new InvalidDataException($"资源路径越界: {relative}");
        }

        var fullRoot = Path.GetFullPath(root);
        var fullPath = Path.GetFullPath(Path.Combine(
            fullRoot,
            relative.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar)));
        if (!IsUnderRoot(fullRoot, fullPath))
        {
            throw new InvalidDataException($"资源路径越界: {relative}");
        }
        return fullPath;
    }

    /// <summary>
    /// 相对路径的形态校验:非空、非绝对、不含 <c>.</c>/<c>..</c> 段。
    /// </summary>
    public static bool IsSafeRelativePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }
        var normalized = path.Replace('\\', '/');
        if (Path.IsPathRooted(normalized) || normalized.StartsWith("/", StringComparison.Ordinal))
        {
            return false;
        }
        return !normalized.Split('/', StringSplitOptions.None)
            .Any(segment => segment is "." or "..");
    }
}
