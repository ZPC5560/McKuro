namespace McKuro.Core.Services.Guide;

/// <summary>
/// mcguide roleGbId 解析。
/// <para>已验证:库街区 getRoleDetail 的 <c>id</c>(cardRoleId) 与 guide 的 <c>roleGbId</c> 是同一套 ID
/// (莫宁 1209、绯雪 1108 双重验证一致),因此直接用 cardRoleId 即可,无需静态角色名登记表。</para>
/// </summary>
public static class GuideRoleMap
{
    /// <summary>用库街区 cardRoleId 直接作为 guide roleGbId(&gt;0 时)。</summary>
    public static string? TryGetRoleGbId(int cardRoleId)
        => cardRoleId > 0 ? cardRoleId.ToString(System.Globalization.CultureInfo.InvariantCulture) : null;

    /// <summary>按角色名查(预留:仅当个别角色 ID 不一致时在此登记覆盖;未知返回 null)。</summary>
    public static string? TryGetRoleGbId(string? roleName)
    {
        if (string.IsNullOrWhiteSpace(roleName))
        {
            return null;
        }
        return Map.TryGetValue(roleName.Trim(), out var gbId) ? gbId : null;
    }

    /// <summary>名称覆盖表(个别角色与 cardRoleId 不一致时登记)。</summary>
    private static readonly IReadOnlyDictionary<string, string> Map = new Dictionary<string, string>(StringComparer.Ordinal);
}
