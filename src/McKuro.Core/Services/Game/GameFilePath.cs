using McKuro.Core.Infrastructure;

namespace McKuro.Core.Services.Game;

/// <summary>游戏资源相对路径的边界校验与安全组合(实现收敛在 <see cref="ArchivePathGuard"/>)。</summary>
internal static class GameFilePath
{
    public static string CombineUnderRoot(string root, string relative)
        => ArchivePathGuard.CombineUnderRoot(root, relative);

    public static bool IsSafeRelativePath(string? path)
        => ArchivePathGuard.IsSafeRelativePath(path);
}
