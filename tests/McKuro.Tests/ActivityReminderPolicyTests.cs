using McKuro.Core.Services.Notification;

namespace McKuro.Tests;

/// <summary>
/// 活动临期提醒的判定与 Key 测试。
/// <para>
/// 回归背景:活动页的「忽略」按钮与提醒调度器的忽略名单判定是两套代码,Key 各写一份。
/// 卡池那条链路没接忽略判定,活动页也没有忽略按钮 —— 用户把活动全忽略了,每次重开软件
/// 照样收到《活动即将结束》(台账里全是 <c>act:pool:...</c>)。本测试把 Key 拼法与临期口径钉住。
/// </para>
/// </summary>
public class ActivityReminderPolicyTests
{
    private static readonly DateTime Now = new(2026, 11, 9, 20, 0, 0, DateTimeKind.Unspecified);

    [Fact]
    public void VersionKey_Matches_Legacy_Pipe_Format()
    {
        // 历史忽略名单就是按这个格式落盘的:改格式会让老用户的忽略记录全部失效
        var key = ActivityReminderPolicy.VersionKey("朝月赠礼",
            new DateTime(2026, 9, 30, 11, 0, 0), new DateTime(2026, 11, 11, 3, 59, 0));
        Assert.Equal("朝月赠礼|202609301100|202611110359", key);
    }

    [Fact]
    public void PoolKey_Is_Prefixed_And_Distinguishable()
    {
        var start = new DateTime(2026, 10, 22, 10, 0, 0);
        var end = new DateTime(2026, 11, 11, 11, 59, 0);
        var pool = ActivityReminderPolicy.PoolKey("沉冥", start, end);
        var version = ActivityReminderPolicy.VersionKey("沉冥", start, end);

        Assert.Equal("pool:沉冥|202610221000|202611111159", pool);
        // 同名同期的版本活动与卡池必须落在两个不同 Key 上,否则忽略一个会连带忽略另一个
        Assert.NotEqual(pool, version);
        Assert.StartsWith(ActivityReminderPolicy.PoolKeyPrefix, pool);
    }

    [Fact]
    public void VersionKey_Fallback_Title_Still_Produces_Stable_Key()
    {
        // 空标题的回退文案由调用方传入(活动页与调度器必须给同一份文案)
        var start = new DateTime(2026, 11, 1, 4, 0, 0);
        var end = new DateTime(2026, 11, 10, 3, 59, 0);
        Assert.Equal("活动|202611010400|202611100359",
            ActivityReminderPolicy.VersionKey("活动", start, end));
        Assert.Equal("|202611010400|202611100359",
            ActivityReminderPolicy.VersionKey(null, start, end));
    }

    [Theory]
    [InlineData(0, true)]      // 恰好结束这一刻仍算临期(与调度器 end < now 才跳过一致)
    [InlineData(1, true)]      // 1 小时
    [InlineData(72, true)]     // 恰好 3 天
    [InlineData(73, false)]    // 超出 3 天
    public void IsExpiring_Uses_Three_Day_Window_And_Includes_End_Instant(int hoursFromNow, bool expected)
    {
        Assert.Equal(expected, ActivityReminderPolicy.IsExpiring(Now.AddHours(hoursFromNow), Now));
    }

    [Fact]
    public void IsExpiring_Rejects_Already_Ended()
    {
        Assert.False(ActivityReminderPolicy.IsExpiring(Now.AddHours(-1), Now));
    }

    [Fact]
    public void IsIgnored_Handles_Null_List_And_Exact_Match()
    {
        var key = ActivityReminderPolicy.PoolKey("存帧",
            new DateTime(2026, 10, 22, 10, 0, 0), new DateTime(2026, 11, 11, 11, 59, 0));

        Assert.False(ActivityReminderPolicy.IsIgnored(null, key));
        Assert.False(ActivityReminderPolicy.IsIgnored([], key));
        Assert.True(ActivityReminderPolicy.IsIgnored([key], key));

        // 前缀不同的 Key 不能互相命中(版本活动 vs 卡池)
        Assert.False(ActivityReminderPolicy.IsIgnored([key[ActivityReminderPolicy.PoolKeyPrefix.Length..]], key));
    }

    [Fact]
    public void PruneExpired_Keeps_Active_And_Recently_Ended_Records()
    {
        var stillRunning = ActivityReminderPolicy.VersionKey("进行中",
            Now.AddDays(-5), Now.AddDays(2));
        var justEnded = ActivityReminderPolicy.VersionKey("刚结束",
            Now.AddDays(-10), Now.AddDays(-1));   // 结束 1 天,仍在保留期内
        var longEnded = ActivityReminderPolicy.PoolKey("早就结束",
            Now.AddDays(-30), Now.AddDays(-10));  // 结束 10 天,超出 7 天保留期

        var kept = ActivityReminderPolicy.PruneExpired([stillRunning, justEnded, longEnded], Now);

        Assert.Equal([stillRunning, justEnded], kept);
    }

    [Fact]
    public void PruneExpired_Keeps_Unparsable_Keys()
    {
        // 判不准就保留:误删用户的忽略记录会直接变成"又弹一次",比留垃圾严重得多
        var kept = ActivityReminderPolicy.PruneExpired(["这不是合法 Key", ""], Now);
        Assert.Equal(["这不是合法 Key"], kept);
    }

    [Fact]
    public void PruneExpired_Removes_Duplicates_And_Handles_Null()
    {
        var key = ActivityReminderPolicy.VersionKey("活动", Now.AddDays(-1), Now.AddDays(1));
        Assert.Equal([key], ActivityReminderPolicy.PruneExpired([key, key], Now));
        Assert.Empty(ActivityReminderPolicy.PruneExpired(null, Now));
    }

    [Fact]
    public void PruneExpired_Reads_End_From_Tail_When_Name_Contains_Separator()
    {
        // 活动名里本身可能带 "|":结束时刻必须从末段读,不能按第一个分隔符切
        var key = ActivityReminderPolicy.VersionKey("A|B", Now.AddDays(-1), Now.AddDays(1));
        Assert.Single(ActivityReminderPolicy.PruneExpired([key], Now));
    }
}
