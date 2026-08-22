using McKuro.ViewModels;

namespace McKuro.Tests;

/// <summary>
/// 每日数据项总量选择逻辑:curOnly 项(千道门扉/周度游历)无总量;
/// 否则优先接口 total,缺失(0)时回退默认上限(活跃度 100 / 周本 3)。
/// </summary>
public class DailyItemTests
{
    [Fact]
    public void ResolveTotal_Prefers_Detail_Total()
    {
        Assert.Equal(160, DailyItem.ResolveTotal(false, 160, 100));
        Assert.Equal(100, DailyItem.ResolveTotal(false, 100, 3));
    }

    [Fact]
    public void ResolveTotal_Falls_Back_When_Detail_Total_Zero()
    {
        Assert.Equal(100, DailyItem.ResolveTotal(false, 0, 100));
        Assert.Equal(3, DailyItem.ResolveTotal(false, 0, 3));
    }

    [Fact]
    public void ResolveTotal_CurOnly_Always_Zero()
    {
        Assert.Equal(0, DailyItem.ResolveTotal(true, 160, 100));
        Assert.Equal(0, DailyItem.ResolveTotal(true, 0, 3));
    }
}
