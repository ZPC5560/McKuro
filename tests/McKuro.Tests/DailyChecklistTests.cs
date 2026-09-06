using McKuro.Core.Models.User;
using McKuro.Core.Services.User;

namespace McKuro.Tests;

/// <summary>
/// 今日日常清单工厂:任务项(签到/活跃度/周本等)完成判定与资源项(体力/结晶单质)进度展示。
/// </summary>
public class DailyChecklistTests
{
    private static RoleDailyData Sample() => new()
    {
        HasSignIn = true,
        LivenessData = new RoleDailyDetail { Cur = 80, Total = 100 },
        LivenessLimit = 100,
        EnergyData = new RoleDailyDetail { Cur = 120, Total = 160 },
        StoreEnergyData = new RoleDailyDetail { Cur = 30, Total = 200 },
        WeeklyData = new RoleDailyDetail { Cur = 2, Total = 3 },
        WeeklyLimit = 3,
        NewTowerData = new RoleDailyDetail { Cur = 1, Total = 1 },
        SlashTowerData = new RoleDailyDetail { Cur = 0, Total = 2 },
        RougeData = new RoleDailyDetail { Cur = 4, Total = 4 },
        WeeklyFrameData = new RoleDailyDetail { Cur = 0 },
    };

    [Fact]
    public void Build_Null_Data_Returns_Empty()
    {
        Assert.Empty(DailyChecklistFactory.Build(null));
    }

    [Fact]
    public void Build_Orders_Tasks_Then_Resources()
    {
        var keys = DailyChecklistFactory.Build(Sample(), bbsGoldCur: 20, bbsGoldMax: 60)
            .Select(e => e.Key)
            .ToList();
        Assert.Equal(
        [
            "GameSign", "KuroBbsTasks", "Liveness", "Weekly",
            "NewTower", "SlashTower", "Rouge", "WeeklyFrame",
            "Energy", "StoreEnergy",
        ], keys);
    }

    [Fact]
    public void Build_GameSign_Matches_HasSignIn()
    {
        var entries = DailyChecklistFactory.Build(Sample());
        var sign = entries.First(e => e.Key == "GameSign");
        Assert.True(sign.IsDone);
        Assert.Equal("已签到", sign.Detail);
        Assert.True(sign.IsExecutable);

        var notSigned = DailyChecklistFactory.Build(new RoleDailyData());
        Assert.False(notSigned.First(e => e.Key == "GameSign").IsDone);
    }

    [Fact]
    public void Build_KuroBbsTasks_Uses_Gold_Progress()
    {
        var entries = DailyChecklistFactory.Build(Sample(), bbsGoldCur: 60, bbsGoldMax: 60);
        var done = entries.First(e => e.Key == "KuroBbsTasks");
        Assert.True(done.IsDone);
        Assert.Equal("库洛币 60/60", done.Detail);
        Assert.True(done.IsExecutable);

        // 上限未知(<=0):无法判定完成,仅显示可执行
        var unknown = DailyChecklistFactory.Build(Sample()).First(e => e.Key == "KuroBbsTasks");
        Assert.False(unknown.IsDone);
        Assert.Equal("—", unknown.Detail);
        Assert.True(unknown.IsExecutable);
    }

    [Fact]
    public void Build_Task_Done_Requires_Cur_Ge_Total()
    {
        var entries = DailyChecklistFactory.Build(Sample());
        Assert.False(entries.First(e => e.Key == "Liveness").IsDone);   // 80/100
        Assert.False(entries.First(e => e.Key == "Weekly").IsDone);     // 2/3
        Assert.True(entries.First(e => e.Key == "NewTower").IsDone);    // 1/1
        Assert.True(entries.First(e => e.Key == "Rouge").IsDone);       // 4/4
        Assert.False(entries.First(e => e.Key == "SlashTower").IsDone); // 0/2
    }

    [Fact]
    public void Build_Total_Zero_Uses_Fallback_Limit()
    {
        var data = Sample();
        data.LivenessData!.Total = 0;    // 接口缺 total → 回退 LivenessLimit
        data.WeeklyData!.Total = 0;      // 接口缺 total → 回退 WeeklyLimit
        var entries = DailyChecklistFactory.Build(data);

        var liveness = entries.First(e => e.Key == "Liveness");
        Assert.Equal("80/100", liveness.Detail);
        Assert.False(liveness.IsDone);

        var weekly = entries.First(e => e.Key == "Weekly");
        Assert.Equal("2/3", weekly.Detail);
        Assert.False(weekly.IsDone);
    }

    [Fact]
    public void Build_Missing_Detail_Shows_Dash_And_Not_Done()
    {
        var data = Sample();
        data.NewTowerData = null;
        var entry = DailyChecklistFactory.Build(data).First(e => e.Key == "NewTower");
        Assert.False(entry.IsDone);
        Assert.Equal("—", entry.Detail);
    }

    [Fact]
    public void Build_Resource_Entries_Never_Done_And_Show_Progress()
    {
        var entries = DailyChecklistFactory.Build(Sample());
        var energy = entries.First(e => e.Key == "Energy");
        Assert.True(energy.IsResource);
        Assert.False(energy.IsDone);
        Assert.False(energy.IsExecutable);
        Assert.True(energy.HasProgress);
        Assert.Equal(75, energy.Percent); // 120/160

        Assert.True(entries.First(e => e.Key == "StoreEnergy").HasProgress); // 30/200
    }

    [Fact]
    public void SummaryText_Marks_Done_With_Check()
    {
        var done = new DailyChecklistEntry("GameSign", "游戏签到", "已签到", IsDone: true, IsExecutable: true, IsResource: false, 0, 0);
        Assert.Equal("✓ 游戏签到 已签到", done.SummaryText);

        var pending = new DailyChecklistEntry("Liveness", "每日活跃度", "80/100", IsDone: false, IsExecutable: false, IsResource: false, 80, 100);
        Assert.Equal("每日活跃度 80/100", pending.SummaryText);
    }
}
