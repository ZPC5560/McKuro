using McKuro.Core.Models.Roles;

namespace McKuro.Core.Services.Roles;

/// <summary>角色数据服务接口。</summary>
public interface IRoleDataService
{
    /// <summary>
    /// 仅从库街区 API 同步角色列表(roleData),<b>不请求任何角色详情</b>(getRoleDetail)。
    /// <para>返回的列表项会合并本地缓存中已存在的完整详情(上次同步/已查看过的角色详情区不空白);
    /// 详情一律在用户点击具体角色时通过 <see cref="LoadRoleDetailAsync"/> 按需单发。</para>
    /// </summary>
    Task<RoleDataLoadResult> LoadRoleListAsync(
        string token,
        string roleId,
        CancellationToken ct = default);

    /// <summary>
    /// 按需拉取单个角色完整详情(getRoleDetail,角色点击时触发;单次请求天然与用户点击节流,
    /// 不同于旧版同步登录时批量串行拉全量)。
    /// <para>库街区对 getRoleDetail 高频接口的风控(<c>{"geeTest":true}</c>)无法通过客户端验证解除
    /// (角色场景不提供验证入口,之前复用登录验证票据已被服务端拒绝——2026-08 实测),
    /// 触发风控时返回 <c>GeeTest=true</c>,由界面提示稍后重试并保留基础信息。</para>
    /// </summary>
    Task<KujiequApiClient.RoleDetailResult> LoadRoleDetailAsync(
        string token,
        string roleId,
        int targetRoleId,
        CancellationToken ct = default);

    /// <summary>从本地游戏缓存或导入文件读取角色数据。</summary>
    RoleDataLoadResult LoadFromLocal();

    /// <summary>从应用数据库缓存读取(校验账号归属:accountId 必须与缓存记录一致)。</summary>
    RoleDataLoadResult LoadFromCache(string accountId, string playerId);
}
