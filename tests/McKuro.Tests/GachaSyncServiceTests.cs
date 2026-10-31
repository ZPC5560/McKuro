using System.Net;
using System.Text;
using System.Text.Json;
using McKuro.Core.Infrastructure;
using McKuro.Core.Models.Gacha;
using McKuro.Core.Services.Game;
using McKuro.Core.Services.Gacha;

namespace McKuro.Tests;

/// <summary>
/// 抽卡同步编排:按查询池号覆盖写入、全部失败时报错、新增数按前后差计算。
/// gmserver-api 用桩 handler 模拟,按请求体里的 cardPoolType 返回不同响应。
/// </summary>
public class GachaSyncServiceTests : IDisposable
{
    private readonly string _tmpDir;
    private readonly AppDatabase _db;
    private readonly GachaRecordStore _store;

    public GachaSyncServiceTests()
    {
        _tmpDir = Path.Combine(Path.GetTempPath(), "McKuro_sync_" + Guid.NewGuid().ToString("N"));
        _db = new AppDatabase(_tmpDir);
        _store = new GachaRecordStore(_db);
    }

    public void Dispose()
    {
        _db.Dispose();
        try { Directory.Delete(_tmpDir, recursive: true); } catch (Exception) { }
    }

    private static readonly GachaRecordRequest Request = new()
    {
        PlayerId = "100000001",
        RecordId = "rid",
        CardPoolId = "cpid",
        ServerId = "sid",
    };

    /// <summary>按 cardPoolType 返回 JSON 的桩 handler。</summary>
    private sealed class PoolHandler(Func<int, string> respond) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = await request.Content!.ReadAsStringAsync(cancellationToken);
            using var doc = JsonDocument.Parse(body);
            int pool = doc.RootElement.GetProperty("cardPoolType").GetInt32();
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(respond(pool), Encoding.UTF8, "application/json"),
            };
        }
    }

    private GachaSyncService CreateService(Func<int, string> respond)
    {
        var api = new GachaApiClient(new HttpClient(new PoolHandler(respond)));
        return new GachaSyncService(api, _store, new GamePathResolver(() => null));
    }

    private const string Empty = """{"code":0,"msg":"","data":[]}""";

    private static string Records(params (string Name, string Type, int Quality, string Time)[] items)
    {
        var data = string.Join(",", items.Select(i =>
            $$"""{"cardPoolType":"角色调谐（常驻池）","resourceId":1,"qualityLevel":{{i.Quality}},"resourceType":"{{i.Type}}","name":"{{i.Name}}","count":1,"time":"{{i.Time}}"}"""));
        return $$"""{"code":0,"msg":"","data":[{{data}}]}""";
    }

    [Fact]
    public async Task Sync_KeepsWeaponsFromRoleResidentPool_InThatPool()
    {
        // 池 3 返回一把 3 星武器,池 4 为空:旧实现写进池 4 后又被池 4 的"先删"抹掉。
        var service = CreateService(pool => pool == (int)CardPoolType.RoleResident
            ? Records(("源能长刃·测肆", "武器", 3, "2024-01-01 10:00:00"))
            : Empty);

        var result = await service.SyncAsync(Request);

        Assert.True(result.IsSuccess);
        var record = Assert.Single(_store.GetRecords(Request.PlayerId, CardPoolType.RoleResident));
        Assert.Equal("源能长刃·测肆", record.Name);
        Assert.Empty(_store.GetRecords(Request.PlayerId, CardPoolType.WeaponsResident));
        Assert.Equal(1, result.Analysis![CardPoolType.RoleResident]!.TotalPulls);
    }

    [Fact]
    public async Task Sync_AllPoolsFail_ReturnsApiErrorWithServerMessage()
    {
        // record_id 过期:每个池都返回错误码。不能报"同步成功 0 条"。
        _store.InsertRecords(Request.PlayerId, [new GachaRecord { Name = "旧", Time = "2024-01-01 10:00:00", QualityLevel = 4 }], CardPoolType.RoleActivity);
        var service = CreateService(_ => """{"code":-1,"msg":"链接已过期","data":null}""");

        var result = await service.SyncAsync(Request);

        Assert.Equal(GachaSyncStatus.ApiError, result.Status);
        Assert.Contains("链接已过期", result.Message);
        Assert.Same(Request, result.Request);
        // 查询失败不删旧缓存
        Assert.Single(_store.GetRecords(Request.PlayerId));
    }

    [Fact]
    public async Task Sync_NewRecords_IsCountDifference_NotRewrittenRows()
    {
        var first = Records(("A", "角色", 4, "2024-01-01 10:00:00"), ("B", "角色", 4, "2024-01-01 10:00:00"));
        var second = Records(("C", "角色", 4, "2024-01-02 10:00:00"), ("A", "角色", 4, "2024-01-01 10:00:00"), ("B", "角色", 4, "2024-01-01 10:00:00"));
        var current = first;
        var service = CreateService(pool => pool == (int)CardPoolType.RoleActivity ? current : Empty);

        var r1 = await service.SyncAsync(Request);
        Assert.Equal(2, r1.NewRecords);

        current = second;
        var r2 = await service.SyncAsync(Request);
        Assert.True(r2.IsSuccess);
        Assert.Equal(1, r2.NewRecords);   // 只多了 C,而不是重写的 3 行
        Assert.Equal(3, r2.TotalRecords);
    }

    [Fact]
    public async Task SyncFromLocalLog_NoGameDir_ReportsGameDirNotSet()
    {
        var service = CreateService(_ => Empty);

        var result = await service.SyncFromLocalLogAsync();

        Assert.Equal(GachaSyncStatus.GameDirNotSet, result.Status);
    }

    [Fact]
    public async Task SyncFromLocalLog_MissingLog_ReportsClientLogNotFound()
    {
        var dir = Path.Combine(_tmpDir, "game");
        Directory.CreateDirectory(dir);
        var api = new GachaApiClient(new HttpClient(new PoolHandler(_ => Empty)));
        var service = new GachaSyncService(api, _store, new GamePathResolver(() => dir));

        var result = await service.SyncFromLocalLogAsync();

        Assert.Equal(GachaSyncStatus.ClientLogNotFound, result.Status);
    }

    [Fact]
    public async Task SyncFromLocalLog_UnreadableLog_ReportsReadFailed_NotKeyMismatch()
    {
        // 读不出内容(这里用空文件模拟)不能报成"日志加密方式已变更,请升级"
        var dir = Path.Combine(_tmpDir, "game2");
        var logPath = Path.Combine(dir, GamePathResolver.ClientLogRelative);
        Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);
        File.WriteAllBytes(logPath, []);
        var api = new GachaApiClient(new HttpClient(new PoolHandler(_ => Empty)));
        var service = new GachaSyncService(api, _store, new GamePathResolver(() => dir));

        var result = await service.SyncFromLocalLogAsync();

        Assert.Equal(GachaSyncStatus.ClientLogReadFailed, result.Status);
    }

    [Fact]
    public async Task SyncFromLocalLog_LogHeldOpenForWriting_IsStillReadable()
    {
        // 游戏运行中持有 Client.log 写句柄:旧实现 File.ReadAllBytes 共享冲突 → 误报"加密方式已变更"
        var dir = Path.Combine(_tmpDir, "game3");
        var logPath = Path.Combine(dir, GamePathResolver.ClientLogRelative);
        Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);
        var plain = new List<byte> { 0xEF, 0xBB, 0xBF };
        plain.AddRange(Encoding.UTF8.GetBytes("Log file open\nno gacha url here\n"));
        var encrypted = plain.Select(b => (b & 1) == 1 ? (byte)(b ^ 0xEF) : (byte)(b ^ 0xA5)).ToArray();

        using (var writer = new FileStream(logPath, FileMode.Create, FileAccess.Write, FileShare.Read | FileShare.Delete))
        {
            writer.Write(encrypted);
            writer.Flush();

            var api = new GachaApiClient(new HttpClient(new PoolHandler(_ => Empty)));
            var service = new GachaSyncService(api, _store, new GamePathResolver(() => dir));
            var result = await service.SyncFromLocalLogAsync();

            // 解密成功、只是没有链接
            Assert.Equal(GachaSyncStatus.RecordUrlNotFound, result.Status);
        }
    }
}
