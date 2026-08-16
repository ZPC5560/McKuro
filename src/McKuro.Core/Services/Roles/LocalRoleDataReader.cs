using McKuro.Core.Models.Roles;
using McKuro.Core.Services.Game;

namespace McKuro.Core.Services.Roles;

/// <summary>
/// 本地角色数据读取器:
/// 1) 从游戏本地 SQLite 缓存 (Client/Saved/LocalStorage/LocalStorage.db) 中扫描 JSON 数据;
/// 2) 支持直接导入 库街区玩家卡 JSON 导出文件。
/// </summary>
public sealed class LocalRoleDataReader
{
    private readonly GamePathResolver _paths;

    public LocalRoleDataReader(GamePathResolver paths)
    {
        _paths = paths;
    }

    /// <summary>
    /// 尝试从游戏本地缓存读取角色数据(尽力而为)。
    /// </summary>
    public IReadOnlyList<RoleDetail> ReadFromLocalStorage()
    {
        var dbPath = _paths.LocalStorageDbPath;
        if (dbPath is null || !File.Exists(dbPath))
        {
            return [];
        }

        var results = new List<RoleDetail>();
        try
        {
            using var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={dbPath}");
            connection.Open();

            // LocalStorage.db 是键值表结构,扫描包含角色数据的 JSON
            using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT name FROM sqlite_master WHERE type='table'
                """;
            var tables = new List<string>();
            using (var reader = command.ExecuteReader())
            {
                while (reader.Read())
                {
                    tables.Add(reader.GetString(0));
                }
            }

            foreach (var table in tables)
            {
                try
                {
                    using var scan = connection.CreateCommand();
                    scan.CommandText = $"SELECT * FROM \"{table}\"";
                    using var dataReader = scan.ExecuteReader();
                    var columns = Enumerable.Range(0, dataReader.FieldCount)
                        .Select(dataReader.GetName)
                        .ToArray();

                    while (dataReader.Read())
                    {
                        foreach (var column in columns)
                        {
                            var value = dataReader.GetValue(dataReader.GetOrdinal(column));
                            if (value is not string text || text.Length < 100)
                            {
                                continue;
                            }

                            var roles = TryParseRoleJson(text);
                            if (roles.Count > 0)
                            {
                                results.AddRange(roles);
                            }
                        }
                    }
                }
                catch (Exception)
                {
                    // 跳过无法读取的表
                }
            }
        }
        catch (Exception)
        {
            return [];
        }

        return results;
    }

    /// <summary>
    /// 从 JSON 文件导入角色数据(支持玩家卡导出)。
    /// </summary>
    public IReadOnlyList<RoleDetail> ReadFromJsonFile(string jsonPath)
    {
        if (!File.Exists(jsonPath))
        {
            return [];
        }
        try
        {
            var text = File.ReadAllText(jsonPath);
            return TryParseRoleJson(text);
        }
        catch (Exception)
        {
            return [];
        }
    }

    private static IReadOnlyList<RoleDetail> TryParseRoleJson(string text)
    {
        var list = new List<RoleDetail>();
        try
        {
            var options = new System.Text.Json.JsonSerializerOptions
            {
                TypeInfoResolver = RoleJsonContext.Default,
            };
            using var doc = System.Text.Json.JsonDocument.Parse(text);
            var root = doc.RootElement;

            System.Text.Json.JsonElement? arrayCandidate = null;
            if (root.ValueKind == System.Text.Json.JsonValueKind.Array)
            {
                arrayCandidate = root;
            }
            else if (root.TryGetProperty("roleData", out var rd) && rd.ValueKind == System.Text.Json.JsonValueKind.Array)
            {
                arrayCandidate = rd;
            }
            else if (root.TryGetProperty("data", out var data))
            {
                if (data.ValueKind == System.Text.Json.JsonValueKind.Array)
                {
                    arrayCandidate = data;
                }
                else if (data.TryGetProperty("roleData", out var rd2) && rd2.ValueKind == System.Text.Json.JsonValueKind.Array)
                {
                    arrayCandidate = rd2;
                }
            }

            if (arrayCandidate is null)
            {
                return list;
            }

            foreach (var element in arrayCandidate.Value.EnumerateArray())
            {
                if (element.TryGetProperty("role", out _))
                {
                    var detail = System.Text.Json.JsonSerializer.Deserialize(
                        element.GetRawText(),
                        RoleJsonContext.Default.RoleDetail);
                    if (detail is not null)
                    {
                        list.Add(detail);
                    }
                }
            }
        }
        catch (Exception)
        {
            // 解析失败返回空
        }
        return list;
    }
}
