using System.Text.Json;
using System.Text.Json.Serialization;
using donet.Core.Infrastructure;
using donet.Core.Models.Roles;

namespace donet.Core.Services.Roles;

/// <summary>角色数据来源。</summary>
public enum RoleDataSource
{
    /// <summary>库街区 API(在线)。</summary>
    Kujiequ,
    /// <summary>本地游戏缓存/导入文件。</summary>
    Local,
    None,
}

/// <summary>角色数据加载结果。</summary>
public sealed class RoleDataLoadResult
{
    public required RoleDataSource Source { get; init; }
    public string? Message { get; init; }
    public IReadOnlyList<RoleDetail> Roles { get; init; } = [];
    public bool IsSuccess => Source != RoleDataSource.None;
}

/// <summary>
/// 角色数据服务:整合库街区 API(在线)与本地数据两种来源,并做本地缓存。
/// </summary>
public sealed class RoleDataService
{
    private readonly KujiequApiClient _api;
    private readonly LocalRoleDataReader _localReader;
    private readonly AppDatabase _db;

    public RoleDataService(KujiequApiClient api, LocalRoleDataReader localReader, AppDatabase db)
    {
        _api = api;
        _localReader = localReader;
        _db = db;
    }

    /// <summary>
    /// 从库街区拉取角色数据(需要 token 与 roleId),并写入缓存。
    /// </summary>
    public async Task<RoleDataLoadResult> LoadFromKujiequAsync(
        string token,
        string roleId,
        bool refreshFirst = true,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return new RoleDataLoadResult { Source = RoleDataSource.None, Message = "未配置库街区 Token" };
        }
        if (string.IsNullOrWhiteSpace(roleId))
        {
            return new RoleDataLoadResult { Source = RoleDataSource.None, Message = "未配置角色 ID" };
        }

        try
        {
            if (refreshFirst)
            {
                await _api.RefreshDataAsync(token, roleId, source: "android", ct).ConfigureAwait(false);
            }

            var roles = await _api.GetRoleDataAsync(token, roleId, source: "android", ct).ConfigureAwait(false);
            if (roles.Count > 0)
            {
                SaveCache(roleId, roles);
            }

            return new RoleDataLoadResult
            {
                Source = RoleDataSource.Kujiequ,
                Roles = roles,
                Message = roles.Count == 0 ? "接口返回空数据" : null,
            };
        }
        catch (Exception ex)
        {
            return new RoleDataLoadResult
            {
                Source = RoleDataSource.None,
                Message = $"库街区请求失败: {ex.Message}",
            };
        }
    }

    /// <summary>从本地数据源加载角色数据。</summary>
    public RoleDataLoadResult LoadFromLocal()
    {
        var roles = _localReader.ReadFromLocalStorage();
        if (roles.Count > 0)
        {
            return new RoleDataLoadResult { Source = RoleDataSource.Local, Roles = roles };
        }
        return new RoleDataLoadResult { Source = RoleDataSource.None, Message = "本地未找到角色数据" };
    }

    /// <summary>读取缓存。</summary>
    public RoleDataLoadResult LoadFromCache(string playerId)
    {
        try
        {
            using var cmd = _db.Connection.CreateCommand();
            cmd.CommandText = "SELECT json FROM role_cache WHERE player_id = $playerId";
            cmd.Parameters.AddWithValue("$playerId", playerId);
            var json = cmd.ExecuteScalar() as string;
            if (string.IsNullOrEmpty(json))
            {
                return new RoleDataLoadResult { Source = RoleDataSource.None, Message = "无缓存" };
            }

            var roles = JsonSerializer.Deserialize(json, RoleJsonContext.Default.ListRoleDetail) ?? [];
            return new RoleDataLoadResult { Source = RoleDataSource.Local, Roles = roles, Message = "来自本地缓存" };
        }
        catch (Exception)
        {
            return new RoleDataLoadResult { Source = RoleDataSource.None, Message = "缓存读取失败" };
        }
    }

    private void SaveCache(string playerId, IReadOnlyList<RoleDetail> roles)
    {
        try
        {
            var json = JsonSerializer.Serialize(roles, RoleJsonContext.Default.ListRoleDetail);
            using var cmd = _db.Connection.CreateCommand();
            cmd.CommandText =
                """
                INSERT INTO role_cache(player_id, json, update_time)
                VALUES ($playerId, $json, $time)
                ON CONFLICT(player_id) DO UPDATE SET json = $json, update_time = $time
                """;
            cmd.Parameters.AddWithValue("$playerId", playerId);
            cmd.Parameters.AddWithValue("$json", json);
            cmd.Parameters.AddWithValue("$time", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            cmd.ExecuteNonQuery();
        }
        catch (Exception)
        {
            // 缓存失败不影响返回
        }
    }
}

[JsonSerializable(typeof(RoleDetail))]
[JsonSerializable(typeof(List<RoleDetail>))]
public sealed partial class RoleCacheJsonContext : JsonSerializerContext;
