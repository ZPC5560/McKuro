using McKuro.Core.Models.Roles;
using McKuro.Core.Services.Roles;
using McKuro.Core.Services.Settings;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace McKuro.Tests;

public class SettingsServiceTests : IDisposable
{
    private readonly string _dir;

    public SettingsServiceTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "McKuro-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* ignore */ }
    }

    [Fact]
    public void Save_And_Reload_RoundTrips()
    {
        var s = new SettingsService(_dir, NullLogger<SettingsService>.Instance);
        s.Current.GameRootDir = @"C:\Games\Wuwa";
        s.Current.DownloadConcurrency = 12;
        s.Save();

        // 重新加载,验证持久化生效
        var s2 = new SettingsService(_dir, NullLogger<SettingsService>.Instance);
        Assert.Equal(@"C:\Games\Wuwa", s2.Current.GameRootDir);
        Assert.Equal(12, s2.Current.DownloadConcurrency);
    }

    /// <summary>
    /// 快速校验开关(对齐 Haiyu 1.4.3 FastVerify)必须默认关闭 ——
    /// 默认开启会让所有老用户在升级后静默换成采样校验,属行为回归。
    /// </summary>
    [Fact]
    public void FastVerify_Defaults_Off_And_RoundTrips()
    {
        var s = new SettingsService(_dir, NullLogger<SettingsService>.Instance);
        Assert.False(s.Current.FastVerifyEnabled);

        s.Current.FastVerifyEnabled = true;
        s.Save();

        var s2 = new SettingsService(_dir, NullLogger<SettingsService>.Instance);
        Assert.True(s2.Current.FastVerifyEnabled);
    }

    [Fact]
    public void SkipVerifyFiles_And_AutoDelete_RoundTrip()
    {        var s = new SettingsService(_dir, NullLogger<SettingsService>.Instance);
        s.Current.SkipVerifyFiles = ["Client/Saved/Logs/Client.log", "Client/Saved/Config/WindowsNoEditor/GameUserSettings.ini"];
        s.Current.AutoSkipVerifyDelete = false;
        s.Save();

        var s2 = new SettingsService(_dir, NullLogger<SettingsService>.Instance);
        Assert.Equal(2, s2.Current.SkipVerifyFiles.Count);
        Assert.Equal("Client/Saved/Logs/Client.log", s2.Current.SkipVerifyFiles[0]);
        Assert.False(s2.Current.AutoSkipVerifyDelete);
    }

    [Fact]
    public void SkipVerifyFiles_Defaults_Empty_And_Delete_True()
    {
        var s = new SettingsService(_dir, NullLogger<SettingsService>.Instance);
        Assert.Empty(s.Current.SkipVerifyFiles);
        Assert.True(s.Current.AutoSkipVerifyDelete);
    }

    [Fact]
    public void Save_Uses_Atomic_TempFile()
    {
        var s = new SettingsService(_dir, NullLogger<SettingsService>.Instance);
        s.Current.Language = "en-US";
        s.Save();

        // 应当只有 settings.json(没有遗留的 .tmp-* 文件)
        var tmp = Directory.GetFiles(_dir, "*.tmp-*");
        Assert.Empty(tmp);
        Assert.True(File.Exists(Path.Combine(_dir, "settings.json")));
    }

    [Fact]
    public void Reload_Picks_Up_External_Change()
    {
        var s = new SettingsService(_dir, NullLogger<SettingsService>.Instance);
        s.Current.GameRootDir = "old";
        s.Save();

        // 模拟外部修改
        File.WriteAllText(Path.Combine(_dir, "settings.json"),
            "{\"GameRootDir\":\"new\",\"DownloadConcurrency\":4}");

        s.Reload();
        Assert.Equal("new", s.Current.GameRootDir);
        Assert.Equal(4, s.Current.DownloadConcurrency);
    }

    /// <summary>
    /// 存量 settings.json 里的空 AppUpdateRepo 必须回填默认仓库。
    /// 回归背景:设置页已移除仓库输入框,空串会让自更新永久显示「未配置更新仓库」
    /// 且用户无法自行改回 —— 旧版本默认值就是空串,属必然踩到的存量数据。
    /// </summary>
    [Fact]
    public void Empty_AppUpdateRepo_Is_Backfilled_From_Default()
    {
        File.WriteAllText(Path.Combine(_dir, "settings.json"),
            """{"AppUpdateRepo":""}""");

        var s = new SettingsService(_dir, NullLogger<SettingsService>.Instance);

        Assert.Equal(AppSettings.DefaultAppUpdateRepo, s.Current.AppUpdateRepo);
        Assert.False(string.IsNullOrWhiteSpace(s.Current.AppUpdateRepo));
    }

    /// <summary>字段完全缺失时同样落到默认仓库(不能因为迁移把默认值变成空)。</summary>
    [Fact]
    public void Missing_AppUpdateRepo_Falls_Back_To_Default()
    {
        File.WriteAllText(Path.Combine(_dir, "settings.json"), """{"GameRootDir":"/games"}""");

        var s = new SettingsService(_dir, NullLogger<SettingsService>.Instance);

        Assert.Equal(AppSettings.DefaultAppUpdateRepo, s.Current.AppUpdateRepo);
    }

    /// <summary>非空值(如用户手改 JSON 指向自己的 fork)必须原样保留,迁移只补空。</summary>
    [Fact]
    public void Custom_AppUpdateRepo_Is_Preserved_And_Trimmed()
    {
        File.WriteAllText(Path.Combine(_dir, "settings.json"),
            """{"AppUpdateRepo":"  someone/fork  "}""");

        var s = new SettingsService(_dir, NullLogger<SettingsService>.Instance);

        Assert.Equal("someone/fork", s.Current.AppUpdateRepo);
    }

    /// <summary>默认值本身必须非空,否则迁移无从回填。</summary>
    [Fact]
    public void Default_AppUpdateRepo_Is_Not_Empty()
    {
        Assert.False(string.IsNullOrWhiteSpace(new AppSettings().AppUpdateRepo));
        Assert.Equal(AppSettings.DefaultAppUpdateRepo, new AppSettings().AppUpdateRepo);
    }

    [Fact]
    public async Task SaveAsync_Writes_Latest_Value()
    {
        var s = new SettingsService(_dir, NullLogger<SettingsService>.Instance);
        s.Current.Language = "en-US";
        await s.SaveAsync();

        var s2 = new SettingsService(_dir, NullLogger<SettingsService>.Instance);
        Assert.Equal("en-US", s2.Current.Language);
    }

    [Fact]
    public async Task SaveAsync_Coalesces_Rapid_Writes()
    {
        var s = new SettingsService(_dir, NullLogger<SettingsService>.Instance);

        // 连续快速调用:合并为一次(或几次)落盘,最后一次内容完整
        var tasks = Enumerable.Range(0, 30).Select(i => Task.Run(async () =>
        {
            s.Current.DownloadConcurrency = i;
            await s.SaveAsync();
        }));
        await Task.WhenAll(tasks);

        var s2 = new SettingsService(_dir, NullLogger<SettingsService>.Instance);
        Assert.InRange(s2.Current.DownloadConcurrency, 0, 29);
        // 不应遗留任何临时文件
        Assert.Empty(Directory.GetFiles(_dir, "*.tmp-*"));
    }

    [Fact]
    public async Task SaveAsync_Then_Save_No_Interference()
    {
        var s = new SettingsService(_dir, NullLogger<SettingsService>.Instance);
        s.Current.GameRootDir = "async-dir";
        var flush = s.SaveAsync();
        s.Current.GameRootDir = "sync-dir";
        s.Save();
        await flush;

        var s2 = new SettingsService(_dir, NullLogger<SettingsService>.Instance);
        // 最后落盘的要么是 async 的版本要么是 sync 的版本,文件必须有效
        Assert.True(s2.Current.GameRootDir is "async-dir" or "sync-dir");
    }
}

public class RoleDetailModelTests
{
    [Fact]
    public void RoleDetail_Defaults_Are_Safe()
    {
        var d = new RoleDetail();
        Assert.Equal("未知角色", d.RoleName);
        Assert.False(d.HasEchoRating);
        Assert.False(d.HasEchoTotal);
        Assert.False(d.HasAttributes);
    }

    [Fact]
    public void RoleDetail_UnlockedChainCount_Counts_IsUnlock()
    {
        var d = new RoleDetail
        {
            Chains = new List<ChainInfo>
            {
                new() { ChainNum = 1, IsUnlock = true },
                new() { ChainNum = 2, IsUnlock = true },
                new() { ChainNum = 3, IsUnlock = false },
            },
        };
        Assert.Equal(2, d.UnlockedChainCount);
    }

    [Fact]
    public void RoleDataLoadResult_IsSuccess_Tracks_Source()
    {
        var ok = new RoleDataLoadResult { Source = RoleDataSource.Kujiequ, Roles = new List<RoleDetail>() };
        var bad = new RoleDataLoadResult { Source = RoleDataSource.None };
        Assert.True(ok.IsSuccess);
        Assert.False(bad.IsSuccess);
    }

    /// <summary>
    /// 回归(用户反馈「角色数据页面下面角色详情的声骸列表缺失声骸总评」):
    /// 声骸总评必须是<b>惰性可算</b>的 —— 从缓存反序列化/赋 PhantomData 后直接绑定就要有值,
    /// 不允许依赖"先调用一次 <see cref="RoleDetail.RefreshEchoRating"/> 才会出现"。
    /// <para>
    /// 旧实现把总评存成由 <c>RefreshEchoRating()</c> 赋值的字段,而该方法只挂在
    /// 「在线详情合并 / 攻略填充 / Wiki 权重回填」三条路径上;纯缓存加载与
    /// "详情已完整 → 跳过在线刷新"都不经过它们 ⇒ <c>HasEchoTotal</c> 恒为 false,
    /// 声骸区整段可见但总评行消失。本测试不看任何刷新调用,只断言直接读即为有值。
    /// </para>
    /// </summary>
    [Fact]
    public void RoleDetail_EchoTotal_Is_Lazy_Without_Explicit_Refresh()
    {
        // 装满 5 个槽位:总评均分按 5 槽计算(空槽计 0),只装 1 件时档位会被拉低
        var d = new RoleDetail
        {
            PhantomData = new PhantomData
            {
                Phantoms = Enumerable.Range(0, 5).Select(_ => new EchoInfo
                {
                    SubProps =
                    [
                        new EchoProp { AttributeName = "暴击", AttributeValue = "10.5%" },
                        new EchoProp { AttributeName = "暴击伤害", AttributeValue = "21%" },
                        new EchoProp { AttributeName = "攻击", AttributeValue = "60" },
                        new EchoProp { AttributeName = "生命", AttributeValue = "580" },
                        new EchoProp { AttributeName = "防御", AttributeValue = "70" },
                    ],
                }).ToList(),
            },
        };

        // 关键:全程不调用 RefreshEchoRating / 任何合并逻辑
        Assert.True(d.HasEchoRating);
        Assert.True(d.HasEchoTotal);
        // 无 mainProps → 兼容口径固定分母 10(双暴受上限压制):暴击1.5+暴伤1.5+攻击1.0+生命0.5+防御0.5 = 5.0 → 单件 50 分 × 5
        Assert.Equal(250.0, d.EchoTotalScore);
        Assert.Equal("毕业", d.EchoTotalGrade);
        Assert.Equal(EchoRatingLevel.SSS, d.EchoTotalLevel);
    }

    /// <summary>RefreshEchoRating 仍要能在 Wiki 权重回填后失效并重算(改权重 ⇒ 分数变化)。</summary>
    [Fact]
    public void RoleDetail_RefreshEchoRating_Recomputes_After_Weight_Backfill()
    {
        var echo = new EchoInfo
        {
            SubProps =
            [
                new EchoProp { AttributeName = "暴击", AttributeValue = "10.5%" },
                new EchoProp { AttributeName = "防御", AttributeValue = "35" },
            ],
        };
        var d = new RoleDetail { PhantomData = new PhantomData { Phantoms = [echo] } };

        // 通用权重兜底(双暴受上限压制):暴击(3→1.5,满) + 防御(1→0.5,半档 35/70) = 1.75 → 17.5 分
        Assert.Equal(17.5, d.EchoTotalScore);

        // 回填 Wiki 权重:暴击降为 1.0、防御升为 2.0 → 1.0 + 1.0 = 2.0 → 20.0 分
        echo.PriorityWeights = new Dictionary<string, double>(StringComparer.Ordinal)
        {
            ["暴击"] = 1.0,
            ["防御"] = 2.0,
        };
        d.RefreshEchoRating();
        Assert.Equal(20.0, d.EchoTotalScore);
    }
}
