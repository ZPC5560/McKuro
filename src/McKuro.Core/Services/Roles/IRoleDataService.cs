using McKuro.Core.Models.Roles;

namespace McKuro.Core.Services.Roles;

/// <summary>角色数据服务接口。</summary>
public interface IRoleDataService
{
    /// <summary>从库街区 API 拉取角色数据并缓存(需要 token 与 roleId)。</summary>
    Task<RoleDataLoadResult> LoadFromKujiequAsync(string token, string roleId, bool refreshFirst = true, CancellationToken ct = default);

    /// <summary>从本地游戏缓存或导入文件读取角色数据。</summary>
    RoleDataLoadResult LoadFromLocal();

    /// <summary>从应用数据库缓存读取(校验账号归属:accountId 必须与缓存记录一致)。</summary>
    RoleDataLoadResult LoadFromCache(string accountId, string playerId);
}
