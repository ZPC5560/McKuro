using McKuro.Core.Models.Roles;

namespace McKuro.Core.Services.Roles;

/// <summary>角色数据服务接口。</summary>
public interface IRoleDataService
{
    /// <summary>从库街区 API 拉取角色数据并缓存(需要 token 与 roleId)。</summary>
    /// <remarks>
    /// 库街区对 getRoleDetail 高频接口的风控(<c>{"geeTest":true}</c>)无法通过客户端验证解除
    /// (角色场景不提供验证入口,之前复用登录验证票据已被服务端拒绝——2026-08 实测),
    /// 因此同步触发风控时不再弹极验验证页,直接回退到上次完整缓存并在返回值中提示。
    /// </remarks>
    Task<RoleDataLoadResult> LoadFromKujiequAsync(
        string token,
        string roleId,
        CancellationToken ct = default);

    /// <summary>从本地游戏缓存或导入文件读取角色数据。</summary>
    RoleDataLoadResult LoadFromLocal();

    /// <summary>从应用数据库缓存读取(校验账号归属:accountId 必须与缓存记录一致)。</summary>
    RoleDataLoadResult LoadFromCache(string accountId, string playerId);
}
