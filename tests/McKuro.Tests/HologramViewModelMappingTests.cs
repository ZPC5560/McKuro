using System.Text.Json;
using System.Text.RegularExpressions;
using McKuro.Core.Models.Tower;
using McKuro.Services;
using McKuro.ViewModels;
using Xunit;

namespace McKuro.Tests;

/// <summary>
/// 全息战略 数据 → 展示项 的映射测试(纯函数部分,不依赖界面)。
/// <para>
/// 覆盖界面上真正会被看到的量:地区菜单顺序与进度、boss 卡片的难度刻度与配色、
/// 选中 boss 后默认停留的档位、以及「已通关 / 未挑战」两套文案。
/// </para>
/// </summary>
public class HologramViewModelMappingTests
{
    private static readonly HologramIndexData Index = JsonSerializer.Deserialize(
        """
        {
          "isUnlock": true, "open": true,
          "wikiUrl": "https://wiki.kurobbs.com/mc/home",
          "challengeList": [
            { "sort": 130,
              "country": { "countryId": 100004, "countryName": "演武",
                "homePageImage": "https://img/region-act.png" },
              "indexList": [
                { "bossId": 32010, "bossName": "万囮牢·朽躯", "bossLevel": 60, "difficulty": 0,
                  "bossHeadIcon": "https://img/h-32010.png", "bossIconUrl": "https://img/p-32010.png",
                  "contryId": 100004, "sort": 110 },
                { "bossId": 31010, "bossName": "达妮娅", "bossLevel": 60, "difficulty": 5,
                  "bossHeadIcon": "https://img/h-31010.png", "bossIconUrl": "https://img/p-31010.png",
                  "contryId": 100004, "sort": 109 }
              ] },
            { "sort": 90,
              "country": { "countryId": 100003, "countryName": "强袭",
                "homePageImage": "https://img/region-strong.png" },
              "indexList": [
                { "bossId": 15010, "bossName": "燎照之骑", "bossLevel": 60, "difficulty": 6,
                  "bossHeadIcon": "https://img/h-15010.png", "bossIconUrl": "https://img/p-15010.png",
                  "contryId": 100003, "sort": 93 }
              ] }
          ]
        }
        """, HologramJsonContext.Default.HologramIndexData)!;

    private static readonly HologramDetailData Details = JsonSerializer.Deserialize(
        """
        {
          "isUnlock": true, "open": true,
          "challengeInfo": {
            "32010": [
              { "bossIconUrl": "https://img/p-32010-1.png", "bossLevel": 60, "challengeId": 3201,
                "difficulty": 1, "passTime": 0 },
              { "bossIconUrl": "https://img/p-32010-6.png", "bossLevel": 100, "challengeId": 3206,
                "difficulty": 6, "passTime": 0 }
            ],
            "31010": [
              { "bossIconUrl": "https://img/p-31010-1.png", "bossLevel": 60, "challengeId": 3101,
                "difficulty": 1, "passTime": 10,
                "roles": [ { "natureId": 1, "roleName": "绯雪", "roleLevel": 90, "roleHeadIcon": "https://img/r1.png" } ] },
              { "bossIconUrl": "https://img/p-31010-5.png", "bossLevel": 90, "challengeId": 3105,
                "difficulty": 5, "passTime": 37,
                "roles": [ { "natureId": 1, "roleName": "绯雪", "roleLevel": 90, "roleHeadIcon": "https://img/r1.png" } ] },
              { "bossIconUrl": "https://img/p-31010-6.png", "bossLevel": 100, "challengeId": 3106,
                "difficulty": 6, "passTime": 0 }
            ],
            "15010": [
              { "bossIconUrl": "https://img/p-15010-6.png", "bossLevel": 100, "challengeId": 1506,
                "difficulty": 6, "passTime": 205,
                "roles": [ { "natureId": 4, "roleName": "卡提希娅", "roleLevel": 90, "roleHeadIcon": "https://img/r9.png" } ] }
            ]
          }
        }
        """, HologramJsonContext.Default.HologramDetailData)!;

    private static List<HologramRegionItem> Regions() => TowerViewModel.BuildHologramRegions(Index, Details);

    [Fact]
    public void Regions_Follow_Index_Sort_Order()
    {
        // 实测降序即 演武 → 同步 → 幻痛 → 强袭;界面四个 logo 菜单按此顺序
        Assert.Equal(["演武", "强袭"], Regions().Select(r => r.Name));
    }

    [Fact]
    public void Region_Carries_Logo_And_Progress()
    {
        var act = Regions().Single(r => r.Name == "演武");
        Assert.Equal(100004, act.CountryId);
        Assert.Equal("https://img/region-act.png", act.LogoUrl);
        Assert.Equal(2, act.BossCount);
        // 演武:32010 未挑战、31010 打到 5 档 → 已打过 1/2,满档 0/2
        Assert.Equal(1, act.ChallengedCount);
        Assert.Equal(0, act.ClearedCount);
        Assert.Equal("1/2", act.ProgressText);
        Assert.Equal(50, act.ProgressPercent);
        Assert.True(act.HasBosses);
    }

    [Fact]
    public void Region_Progress_Counts_Bosses_With_Any_Clear()
    {
        var strong = Regions().Single(r => r.Name == "强袭");
        // 强袭唯一 boss 已满档 → 已打过 1/1,同时也是满档 1/1
        Assert.Equal(1, strong.ChallengedCount);
        Assert.Equal(1, strong.ClearedCount);
        Assert.Equal("1/1", strong.ProgressText);
        Assert.Equal(100, strong.ProgressPercent);
        // 满档数只在悬停提示里出现(菜单不再显示「满档」二字)
        Assert.Contains("满档", strong.TooltipText, StringComparison.Ordinal);
    }

    [Fact]
    public void Region_Progress_Is_Zero_Without_Bosses()
    {
        var empty = TowerViewModel.BuildHologramRegions(
            JsonSerializer.Deserialize(
                """
                {"challengeList":[{"sort":5,"country":{"countryId":9,"countryName":"空区"},
                "indexList":[]}]}
                """, HologramJsonContext.Default.HologramIndexData),
            null);
        var region = Assert.Single(empty);
        Assert.Equal(0, region.BossCount);
        Assert.Equal(0, region.ProgressPercent);  // 不能除零
        Assert.False(region.HasBosses);
    }

    [Fact]
    public void Boss_Card_Shows_Cleared_Tier_And_Six_Pips()
    {
        var dalnya = Regions().Single(r => r.Name == "演武").Bosses.Single(b => b.BossId == 31010);

        Assert.Equal(5, dalnya.ClearedDifficulty);
        Assert.False(dalnya.IsFullyCleared);
        Assert.True(dalnya.HasAnyClear);
        // 与本文件其他文案断言同口径:走 LanguageService,不硬编码中文,
        // 避免测试进程语言若被改动时这里脆断(zh-Hans 模板 "难度 {0}/{1}")
        Assert.Equal(LanguageService.Format("Tower.Holo.DifficultyOf", 5, 6), dalnya.DifficultyText);
        // 6 格刻度恒在(缺格会让"5/6"与"5/5"分不清),前 5 格已通关
        Assert.Equal(6, dalnya.Pips.Count);
        Assert.Equal([true, true, true, true, true, false], dalnya.Pips.Select(p => p.Filled));
        Assert.Equal([1, 2, 3, 4, 5, 6], dalnya.Pips.Select(p => p.Tier));
        // 只有第 6 格是"最高档"(满档时用更亮一档的金色)
        Assert.Equal([false, false, false, false, false, true], dalnya.Pips.Select(p => p.IsMaxTier));
    }

    [Fact]
    public void Boss_Card_Unchallenged_Uses_NotChallenged_Copy()
    {
        var boss = Regions().Single(r => r.Name == "演武").Bosses.Single(b => b.BossId == 32010);
        Assert.Equal(0, boss.ClearedDifficulty);
        Assert.Equal(LanguageService.Format("Tower.Holo.NotChallenged"), boss.DifficultyText);
        Assert.All(boss.Pips, p => Assert.False(p.Filled));
        Assert.False(boss.IsFullyCleared);
        // 未挑战:难度文案走"未挑战"态(次级灰)
        Assert.False(boss.HasAnyClear);
    }

    [Fact]
    public void Boss_Card_Fully_Cleared_Is_Marked_By_Pips_And_Text_Not_A_Frame()
    {
        var boss = Regions().Single(r => r.Name == "强袭").Bosses.Single(b => b.BossId == 15010);
        Assert.True(boss.IsFullyCleared);
        Assert.True(boss.HasAnyClear);
        Assert.Equal(LanguageService.Format("Tower.Holo.DifficultyOf", 6, 6), boss.DifficultyText);
        Assert.All(boss.Pips, p => Assert.True(p.Filled));

        // 回归:网格卡片曾给满档 boss 加一圈金色描边(BorderColor),用户要求去掉 ——
        // "打满了"改由 6 格刻度 + 难度文案表达。
        // 断言模型层不再有这个成员(而不是"XAML 里搜不到这个词",后者被注释误伤就会假红)。
        Assert.Null(typeof(HologramBossItem).GetProperty("BorderColor"));
    }

    [Fact]
    public void Boss_Card_Difficulty_Text_Color_Comes_From_Theme_Not_Hardcoded_Hex()
    {
        // 回归:难度文案颜色曾在 VM 里硬编码(#E8E8E8 / #8A8F99),
        // 浅色主题下浅灰压浅底几乎看不见(用户反馈"当期难度文本在白色主题下不明显")。
        // 现在只暴露"是否已挑战"这个状态,配色交给 XAML 的主题令牌。
        var withClear = Regions().SelectMany(r => r.Bosses).First(b => b.HasAnyClear);
        var withoutClear = Regions().SelectMany(r => r.Bosses).First(b => !b.HasAnyClear);
        Assert.NotEqual(withClear.HasAnyClear, withoutClear.HasAnyClear);

        // 颜色不再是 VM 的职责:VM 不该再暴露任何色值字符串
        Assert.Null(typeof(HologramBossItem).GetProperty("DifficultyColor"));

        // 界面令牌必须主题相关,且 **Dark/Light 两个字典里各有一份** ——
        // 只数"全文件出现 2 次"是不够的:两份都写在 Dark、Light 缺失时也会通过。
        foreach (var token in new[] { "McKuroHoloGold", "McKuroHoloGoldSoft", "McKuroHoloPipEmpty" })
        {
            Assert.True(ThemeDictionaryHasKey("Dark", token), $"Dark 主题字典缺少令牌: {token}");
            Assert.True(ThemeDictionaryHasKey("Light", token), $"Light 主题字典缺少令牌: {token}");
        }
    }

    /// <summary>
    /// 某主题字典块内是否定义了指定令牌。
    /// <para>按 <c>&lt;ResourceDictionary x:Key="Dark"&gt;…&lt;/ResourceDictionary&gt;</c> 切块后查找,
    /// 这样才能真正区分"定义在哪个主题下"。</para>
    /// </summary>
    private static bool ThemeDictionaryHasKey(string theme, string key)
    {
        var app = File.ReadAllText(Path.Combine(RepositoryRoot(), "src", "McKuro", "App.axaml"));
        var start = app.IndexOf("<ResourceDictionary x:Key=\"" + theme + "\">", StringComparison.Ordinal);
        if (start < 0)
        {
            return false;
        }
        var end = app.IndexOf("</ResourceDictionary>", start, StringComparison.Ordinal);
        if (end < 0)
        {
            return false;
        }
        var block = app[start..end];
        return block.Contains("x:Key=\"" + key + "\"", StringComparison.Ordinal);
    }

    /// <summary>定位仓库根(src/McKuro 所在目录)。</summary>
    private static string RepositoryRoot()
    {
        var dir = AppContext.BaseDirectory;
        for (var i = 0; i < 8 && dir is not null; i++)
        {
            if (Directory.Exists(Path.Combine(dir, "src", "McKuro")))
            {
                return dir;
            }
            dir = Directory.GetParent(dir)?.FullName;
        }
        throw new DirectoryNotFoundException("未能定位仓库根");
    }

    [Fact]
    public void Boss_Card_Carries_Art_And_Tiers()
    {
        var boss = Regions().Single(r => r.Name == "演武").Bosses.Single(b => b.BossId == 31010);
        Assert.Equal("https://img/h-31010.png", boss.HeadIcon);
        Assert.Equal("https://img/p-31010.png", boss.HeroIcon);
        Assert.Equal(3, boss.Tiers.Count); // 该 boss 只有 3 档有记录
    }

    [Fact]
    public void Pip_States_Encode_Filled_And_Max_Known_By_Xaml_Classes()
    {
        // 刻度格不再携带色值(配色走 XAML 的 Classes + 主题令牌),
        // 但要保证 XAML 依赖的两个状态在数据里都可判定:
        //   filled → 已通关;max → 是不是最高档(用于满档那一格更亮一档)
        var boss = Regions().Single(r => r.Name == "演武").Bosses.Single(b => b.BossId == 31010);
        Assert.Equal(6, boss.Pips.Count);
        Assert.Contains(boss.Pips, p => p.Filled);
        Assert.Contains(boss.Pips, p => !p.Filled);
        Assert.Single(boss.Pips, p => p.IsMaxTier);
        Assert.Equal(6, boss.Pips.Single(p => p.IsMaxTier).Tier);

        // 界面上真正用到的 Classes 组合必须与样式选择器对得上
        var xaml = File.ReadAllText(Path.Combine(RepositoryRoot(), "src", "McKuro", "Views", "TowerView.axaml"));
        Assert.Contains("Selector=\"Border.holoPip\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Selector=\"Border.holoPip.filled\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Selector=\"Border.holoPip.filled.max\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void Tier_Rows_Carry_State_Text_For_Vertical_List()
    {
        // 难度在详情里是竖向列表,每行自带状态文案,不需要用户去数刻度点
        var boss = Regions().Single(r => r.Name == "演武").Bosses.Single(b => b.BossId == 31010);
        var tiers = TowerViewModel.BuildHologramTiers(boss);

        Assert.Equal(6, tiers.Count);
        Assert.Equal([1, 2, 3, 4, 5, 6], tiers.Select(t => t.Difficulty));
        // 该 boss 难度 1 与 5 有记录
        Assert.True(tiers.Single(t => t.Difficulty == 1).IsCleared);
        Assert.True(tiers.Single(t => t.Difficulty == 5).IsCleared);
        Assert.False(tiers.Single(t => t.Difficulty == 6).IsCleared);
        Assert.Equal(LanguageService.Format("Tower.Holo.TierCleared"),
            tiers.Single(t => t.Difficulty == 5).StateText);
        Assert.Equal(LanguageService.Format("Tower.Holo.TierNotCleared"),
            tiers.Single(t => t.Difficulty == 6).StateText);
    }

    [Fact]
    public void Tier_Rows_All_Uncleared_For_Boss_Without_Records()
    {
        var boss = Regions().Single(r => r.Name == "演武").Bosses.Single(b => b.BossId == 32010);
        var tiers = TowerViewModel.BuildHologramTiers(boss);
        Assert.Equal(6, tiers.Count);            // 6 档恒定全出
        Assert.All(tiers, t => Assert.False(t.IsCleared));
        Assert.All(tiers, t => Assert.Equal(
            LanguageService.Format("Tower.Holo.TierNotCleared"), t.StateText));
        // 无记录时仍能构建(空 boss 也不抛)
        Assert.Equal(6, TowerViewModel.BuildHologramTiers(null).Count);
    }

    [Fact]
    public void Default_Difficulty_Stops_At_Highest_Cleared()
    {
        var bosses = Regions().SelectMany(r => r.Bosses).ToList();
        var withRecord = bosses.Single(b => b.BossId == 31010);
        var without = bosses.Single(b => b.BossId == 32010);

        Assert.Equal(5, TowerViewModel.DefaultHologramDifficulty(withRecord));
        Assert.Equal(1, TowerViewModel.DefaultHologramDifficulty(without)); // 没记录 → 回到难度 1
        Assert.Equal(1, TowerViewModel.DefaultHologramDifficulty(null));
    }

    [Fact]
    public void Difficulty_Set_Externally_Keeps_Tier_List_And_Detail_In_Sync()
    {
        // 回归:`SelectedHologramDifficulty` 是公开可绑定属性;
        // 早期只有「点 boss / 点档位项」两个入口会刷新界面,直接给该属性赋值会
        // 留下"档位值已变、列表选中项与右栏详情仍是旧档"的错位,直到用户再点一次才自愈。
        // 这里直接走属性赋值(模拟外部改动),要求列表选中项与详情同步跟上。
        var vm = new TowerViewModel();
        vm.SelectedHologramBoss = Regions().Single(r => r.Name == "演武").Bosses.Single(b => b.BossId == 31010);

        // 该 boss 已通关最高档为 5 → 默认停在 5
        Assert.Equal(5, vm.SelectedHologramDifficulty);
        Assert.Equal(5, vm.SelectedHologramTier?.Difficulty);

        // 外部改到未通关的难度 6
        vm.SelectedHologramDifficulty = 6;
        Assert.Equal(6, vm.SelectedHologramTier?.Difficulty);
        Assert.False(vm.HologramTierCleared);
        Assert.Equal("", vm.HologramPassTimeText);
        Assert.Empty(vm.HologramRoles);

        // 外部改回已通关的难度 1 → 时间与队伍要跟着出来
        vm.SelectedHologramDifficulty = 1;
        Assert.Equal(1, vm.SelectedHologramTier?.Difficulty);
        Assert.True(vm.HologramTierCleared);
        Assert.Equal("00:10", vm.HologramPassTimeText);
        Assert.NotEmpty(vm.HologramRoles);
    }

    [Fact]
    public void Regions_Handle_Empty_Index()
    {
        var empty = TowerViewModel.BuildHologramRegions(
            JsonSerializer.Deserialize("""{"challengeList":[]}""", HologramJsonContext.Default.HologramIndexData),
            null);
        Assert.Empty(empty);
    }

    [Fact]
    public void Regions_Handle_Missing_Details()
    {
        // 详情接口失败时地区与卡片仍要能出来(只是刻度全空、Tiers 为空)
        var regions = TowerViewModel.BuildHologramRegions(Index, null);
        Assert.Equal(2, regions.Count);
        Assert.All(regions.SelectMany(r => r.Bosses), b => Assert.Empty(b.Tiers));
        // 此时刻度仍按索引里的 difficulty 显示,不会退化成"全未挑战"
        var dalnya = regions.Single(r => r.Name == "演武").Bosses.Single(b => b.BossId == 31010);
        Assert.Equal(5, dalnya.ClearedDifficulty);
    }

    [Fact]
    public void Missing_Details_Marks_Records_Unavailable_And_Keeps_Tiers_Consistent()
    {
        // 回归(审查 I-1):challengeIndex 成功、challengeDetails 失败是一条必然可达的降级路径
        // (两个接口各自独立 catch)。此时:
        //   ① 卡片上的 6 格刻度仍要按索引显示"打到第 5 档";
        //   ② 右栏的档位列表**不能**全渲染成「未通关」—— 否则同一屏左边说 5/6、右边说一档没过;
        //   ③ 通关时间区要显示"记录暂不可用",而不是伪造一个用时或说"没打过"。
        var boss = TowerViewModel.BuildHologramRegions(Index, null)
            .Single(r => r.Name == "演武").Bosses.Single(b => b.BossId == 31010);

        Assert.False(boss.RecordsAvailable);
        Assert.Equal(5, boss.ClearedDifficulty);
        Assert.All(boss.Pips.Take(5), p => Assert.True(p.Filled));
        Assert.False(boss.Pips[5].Filled);

        var tiers = TowerViewModel.BuildHologramTiers(boss);
        Assert.Equal(6, tiers.Count);
        // 前 5 档按索引回填为已通关(与左侧刻度一致)
        Assert.Equal([true, true, true, true, true, false], tiers.Select(t => t.IsCleared));
        Assert.Equal(LanguageService.Format("Tower.Holo.TierCleared"), tiers[0].StateText);
        Assert.Equal(LanguageService.Format("Tower.Holo.TierNotCleared"), tiers[5].StateText);
    }

    [Fact]
    public void Unchallenged_Boss_With_Records_Available_Still_Shows_All_Uncleared()
    {
        // 反向守卫:详情**可用**且确实没打过时,6 档必须全是「未通关」
        // (别让上面的降级回填把"真没打过"也一起点亮)
        var boss = TowerViewModel.BuildHologramRegions(Index, Details)
            .Single(r => r.Name == "演武").Bosses.Single(b => b.BossId == 32010);

        Assert.True(boss.RecordsAvailable);
        Assert.Equal(0, boss.ClearedDifficulty);
        Assert.All(TowerViewModel.BuildHologramTiers(boss), t => Assert.False(t.IsCleared));
    }

    [Fact]
    public void Region_Change_Selects_First_Boss_And_Syncs_Selection_Flags()
    {
        // 覆盖审查 M-5 指出的空白:切地区 → 自动选该地区第一个 boss,且两个列表的
        // IsSelected 标记(供 XAML Classes.active 绑定)随之同步。
        //
        // 注意:VM 的 HologramRegions 平时由 LoadAsync 填充,测试里没有 DI 容器不会自动填,
        // 故先把地区塞进 VM(否则 SyncHologramSelection 遍历空集合,断言会假绿)。
        var vm = new TowerViewModel();
        var regions = Regions();
        foreach (var r in regions)
        {
            vm.HologramRegions.Add(r);
        }
        var act = regions.Single(r => r.Name == "演武");

        vm.SelectedHologramRegion = act;

        // 网格填的是该地区的 boss
        Assert.Equal(act.Bosses.Count, vm.HologramBosses.Count);
        // 自动选中第一个
        Assert.Same(act.Bosses[0], vm.SelectedHologramBoss);
        // 选中标记:仅当前地区/当前 boss 为 true
        Assert.All(vm.HologramRegions, r => Assert.Equal(ReferenceEquals(r, act), r.IsSelected));
        Assert.All(vm.HologramBosses, b => Assert.Equal(ReferenceEquals(b, vm.SelectedHologramBoss), b.IsSelected));

        // 换到另一个地区后,旧地区的标记要清掉(否则会出现两个高亮)
        var strong = regions.Single(r => r.Name == "强袭");
        vm.SelectedHologramRegion = strong;
        Assert.False(act.IsSelected);
        Assert.True(strong.IsSelected);
        Assert.DoesNotContain(vm.HologramBosses, b => ReferenceEquals(b, act.Bosses[0]));
        Assert.True(vm.HologramBosses[0].IsSelected);
    }

    [Fact]
    public void Region_Change_Falls_Back_To_First_Boss_Of_New_Region()
    {
        // 切地区必须换掉右栏的主体:新地区的首个 boss 被选中,
        // 且档位列表按新 boss 重建(否则右栏还停在上一个 boss 的记录上)
        var vm = new TowerViewModel();
        var regions = Regions();
        foreach (var r in regions)
        {
            vm.HologramRegions.Add(r);
        }
        var act = regions.Single(r => r.Name == "演武");
        var strong = regions.Single(r => r.Name == "强袭");

        vm.SelectedHologramRegion = act;
        var firstBossName = vm.HologramBossName;
        Assert.NotEqual("", firstBossName);

        vm.SelectedHologramRegion = strong;

        Assert.Same(strong.Bosses[0], vm.SelectedHologramBoss);
        Assert.NotEqual(firstBossName, vm.HologramBossName);
        Assert.Equal(TowerViewModel.DefaultHologramDifficulty(strong.Bosses[0]), vm.SelectedHologramDifficulty);
    }

    [Fact]
    public void ShowEmpty_Is_Only_True_When_Unlocked_Without_Bosses()
    {
        // 覆盖审查 M-5:「已解锁但本期无数据」占位面板的触发条件
        var vm = new TowerViewModel();
        Assert.False(vm.HologramShowEmpty);          // 初始:未解锁 → 显示的是「尚未解锁」

        vm.HologramUnlocked = true;
        Assert.True(vm.HologramShowEmpty);           // 已解锁 + 无数据 → 显示「本期暂无数据」

        vm.HologramHasData = true;
        Assert.False(vm.HologramShowEmpty);          // 有数据 → 正常展示
    }

    [Fact]
    public void HologramShowNoRecord_Is_False_When_Records_Unavailable()
    {
        // 「暂无挑战记录」与「记录暂不可用」是两件事:记录拿不到时不能显示前者
        var vm = new TowerViewModel();
        vm.HologramRecordsAvailable = true;
        vm.HologramTierCleared = false;
        Assert.True(vm.HologramShowNoRecord);        // 记录可用 + 未通关 → 「暂无挑战记录」

        vm.HologramRecordsAvailable = false;
        Assert.False(vm.HologramShowNoRecord);       // 记录不可用 → 改显「挑战记录暂不可用」

        vm.HologramRecordsAvailable = true;
        vm.HologramTierCleared = true;
        Assert.False(vm.HologramShowNoRecord);       // 已通关 → 显示用时
    }
}
