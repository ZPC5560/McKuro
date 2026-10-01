using System.Text.Json;
using McKuro.Core.Infrastructure;
using McKuro.Core.Models.Tower;

namespace McKuro.Tests;

/// <summary>
/// 终焉矩阵历史「落库 → 读取」往返测试。
/// <para>
/// 回归背景:矩阵页点「往期历史」右栏永远停在「当前版本等待开放中」,上期记录不显示。
/// 根因是 TowerViewModel 的 <c>_newTowerRoleId</c> 字段从未赋值(恒为空串),
/// 而 <c>LoadNewTowerHistoryDetailAsync</c> 用它去查 <c>new_tower_history</c> ——
/// 空 roleId 必然查不到行。本测试锁定该表的读写契约:同一个 roleId + endTime 必须能取回写入的 JSON,
/// roleId 为空则取不到(即"没接线就必然读空")。
/// </para>
/// </summary>
public class NewTowerHistoryStoreTests : IDisposable
{
    private readonly string _tmpDir;

    public NewTowerHistoryStoreTests()
    {
        _tmpDir = Path.Combine(Path.GetTempPath(), "McKuro_nth_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tmpDir);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_tmpDir, recursive: true);
        }
        catch (Exception)
        {
            // 忽略清理失败
        }
    }

    private const string RoleId = "103242935";

    /// <summary>期末绝对时间(2026-09-30 04:00 本地),与实机落库值同量级。</summary>
    private static readonly long EndTime = new DateTimeOffset(
        new DateTime(2026, 9, 30, 4, 0, 0, DateTimeKind.Local)).ToUnixTimeMilliseconds();

    private static string ModesJson()
        => JsonSerializer.Serialize(
            new List<NewTowerModeDetail>
            {
                new() { ModeId = 1, Score = 44065, Rank = 5, HasRecord = true },
                new() { ModeId = 0, Score = 10823, Rank = 3, HasRecord = true },
            },
            TowerJsonContext.Default.ListNewTowerModeDetail);

    [Fact]
    public void Upsert_Then_Read_By_RoleId_And_EndTime_RoundTrips()
    {
        using var db = new AppDatabase(_tmpDir);

        // 直接走表的读写契约(与 TowerService.SaveNewTowerHistory 落库结构一致)
        db.UpsertNewTowerHistory(RoleId, EndTime, ModesJson());

        var ends = db.GetNewTowerHistoryEndTimes(RoleId);
        Assert.Contains(EndTime, ends);

        var json = db.GetNewTowerHistory(RoleId, EndTime);
        Assert.NotNull(json);
        var modes = JsonSerializer.Deserialize(json!, TowerJsonContext.Default.ListNewTowerModeDetail);
        Assert.Equal(2, modes!.Count);
        Assert.Equal(44065, modes[0].Score);
    }

    [Fact]
    public void Read_With_Empty_RoleId_Returns_Null()
    {
        using var db = new AppDatabase(_tmpDir);
        db.UpsertNewTowerHistory(RoleId, EndTime, ModesJson());

        // 这正是修复前的线上行为:VM 用未赋值的空 roleId 读 → 必然为空 → 右栏一直显示空态。
        // 该用例用于说明"历史查不到"是接线问题,而非数据没落库。
        Assert.Null(db.GetNewTowerHistory("", EndTime));
        Assert.Empty(db.GetNewTowerHistoryEndTimes(""));
    }

    [Fact]
    public void Upsert_Same_Role_And_EndTime_Updates_In_Place()
    {
        using var db = new AppDatabase(_tmpDir);
        db.UpsertNewTowerHistory(RoleId, EndTime, ModesJson());
        db.UpsertNewTowerHistory(RoleId, EndTime, JsonSerializer.Serialize(
            new List<NewTowerModeDetail> { new() { ModeId = 1, Score = 50000, Rank = 5, HasRecord = true } },
            TowerJsonContext.Default.ListNewTowerModeDetail));

        // 同期 UPSERT 不产生重复行(历史列表每期只出现一次)
        Assert.Single(db.GetNewTowerHistoryEndTimes(RoleId));
        var modes = JsonSerializer.Deserialize(db.GetNewTowerHistory(RoleId, EndTime)!,
            TowerJsonContext.Default.ListNewTowerModeDetail);
        Assert.Equal(50000, Assert.Single(modes!).Score);
    }

    [Fact]
    public void EndTimes_Are_Sorted_Descending()
    {
        using var db = new AppDatabase(_tmpDir);
        var older = EndTime - TimeSpan.FromDays(30).Ticks / TimeSpan.TicksPerMillisecond;
        db.UpsertNewTowerHistory(RoleId, older, ModesJson());
        db.UpsertNewTowerHistory(RoleId, EndTime, ModesJson());

        var ends = db.GetNewTowerHistoryEndTimes(RoleId);
        Assert.Equal([EndTime, older], ends);
    }
}
