namespace McKuro.Core.Services.Game;

/// <summary>游戏资源相对路径的边界校验与安全组合。</summary>
internal static class GameFilePath
{
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
        var prefix = fullRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        if (!fullPath.StartsWith(prefix, PathComparison))
        {
            throw new InvalidDataException($"资源路径越界: {relative}");
        }
        return fullPath;
    }

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

    private static StringComparison PathComparison =>
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
}
