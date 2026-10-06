using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using McKuro.Core.Models.Tower;
using McKuro.Services;

namespace McKuro.ViewModels;

/// <summary>逆境深塔-难度展示项(选择器)。</summary>
public sealed class TowerDifficultyItem
{
    public required string DifficultyName { get; init; }
    public required int Difficulty { get; init; }
    /// <summary>最高难度(difficulty==3)显示赛季刷新倒计时(对齐 WutheringWavesTool TowerView)。</summary>
    public bool ShowSeasonEnd => Difficulty == 3;
    public required List<TowerAreaItem> Areas { get; init; }
}

/// <summary>逆境深塔-区域展示项。</summary>
public sealed class TowerAreaItem
{
    public required string AreaName { get; init; }
    /// <summary>已得星/总星,如 "12 / 15"。</summary>
    public required string StarText { get; init; }
    public required List<TowerFloorItem> Floors { get; init; }
}

/// <summary>逆境深塔-楼层展示项。</summary>
public sealed class TowerFloorItem
{
    public required string FloorName { get; init; }
    /// <summary>已得星(0-3)。</summary>
    public required int Star { get; init; }
    /// <summary>已得星字符串,如 "★★"。</summary>
    public string FilledStars => new('★', Star);
    /// <summary>未得星字符串,如 "☆"。</summary>
    public string EmptyStars => new('☆', 3 - Star);
    public required List<TowerSeasonRole> Roles { get; init; }
}

/// <summary>海墟/终焉矩阵 buff 展示项。</summary>
public sealed class TowerBuffItem
{
    public required string BuffName { get; init; }
    public string? BuffIcon { get; init; }
    public string? BuffDescription { get; init; }
}

/// <summary>终焉矩阵模式展示项。</summary>
public sealed class TowerModeItem
{
    public required string ModeName { get; init; }
    public required string ScoreText { get; init; }
    /// <summary>通关进度,如 "2/5"。</summary>
    public required string ProgressText { get; init; }
    public required string RankText { get; init; }
    public required string RankColor { get; init; }
    public required List<NewTowerRole> Roles { get; init; }
    public required List<TowerBuffItem> Buffs { get; init; }
    /// <summary>按轮次分组的队伍明细(每队一条:轮次/该队积分/进度/角色/增益)。</summary>
    public List<TowerRoundItem> Rounds { get; init; } = [];
}

/// <summary>终焉矩阵-一轮(一轮内可有多支队伍)。</summary>
public sealed class TowerRoundItem
{
    /// <summary>轮次标题,如 "第1轮";无轮次信息时为空(不显示)。</summary>
    public required string RoundText { get; init; }
    /// <summary>本轮合计积分。</summary>
    public required string RoundScoreText { get; init; }
    /// <summary>本轮队伍列表。</summary>
    public required List<TowerTeamItem> Teams { get; init; }
}

/// <summary>终焉矩阵-单支队伍(矩阵按轮次分队,每队有独立积分)。</summary>
public sealed class TowerTeamItem
{
    /// <summary>队伍序号文本(按该轮内顺序,如 "第1队")。</summary>
    public required string TeamNoText { get; init; }
    public required string ScoreText { get; init; }
    /// <summary>该队进度,如 "3/5"。</summary>
    public required string PassText { get; init; }
    public required List<NewTowerRole> Roles { get; init; }
    public required List<TowerBuffItem> Buffs { get; init; }
}

/// <summary>终焉矩阵往期历史条目(一期,按赛季结束时间标识,对齐 WutheringWavesTool initHistory)。</summary>
public sealed class NewTowerHistoryItem
{
    public required long EndTimeMillis { get; init; }
    /// <summary>列表文案,如 "2026.08.01 前的记录"。</summary>
    public required string Label { get; init; }
    /// <summary>该期起始日期(按赛季周期由结束日倒推),如 "2026.09.02"。</summary>
    public required string StartDateText { get; init; }
    /// <summary>该期结束日期,如 "2026.09.30"。</summary>
    public required string EndDateText { get; init; }
    /// <summary>时间区间文案,如 "2026.09.02 → 2026.09.30"。</summary>
    public string PeriodText => $"{StartDateText} → {EndDateText}";
}

/// <summary>海墟-单个队伍(半分)展示项。</summary>
public sealed class SlashTeamItem
{
    public required string TeamName { get; init; }
    public required string ScoreText { get; init; }
    public required string BuffName { get; init; }
    public string? BuffIcon { get; init; }
    public string? BuffDescription { get; init; }
    public required List<NewTowerRole> Roles { get; init; }
}

/// <summary>海墟关卡展示项。</summary>
public sealed class SlashChallengeItem
{
    /// <summary>关卡编号(challengeId,如 7..12)。</summary>
    public required int ChallengeId { get; init; }
    /// <summary>编号文本,如 "第7关"。</summary>
    public required string ChallengeNoText { get; init; }
    public required string ChallengeName { get; init; }
    public required string ScoreText { get; init; }
    public required string RankText { get; init; }
    public required string RankColor { get; init; }
    /// <summary>逐队(上半/下半)展示。</summary>
    public required List<SlashTeamItem> Teams { get; init; }
}

/// <summary>
/// 全息战略-难度刻度的一格(签名元素:把 6 档难度画成可读的「已打到第几档」量尺)。
/// <para>6 格始终全部渲染,已通关的格上色、未通关的格压暗成底色 ——
/// 只画实心格会看不出总量,「5/6」与「5/5」就分不清了。</para>
/// <para>
/// 只暴露状态(是否通关/是否满档那一格),<b>不暴露具体颜色</b>:配色交给 XAML 的
/// <c>Classes</c> + 主题令牌(<c>McKuroHoloGold</c> 等),这样亮/暗两个主题各取一份,
/// 不会像硬编码浅金那样在白底上看不清。
/// </para>
/// </summary>
public sealed class HologramPip
{
    /// <summary>该档是否已通关。</summary>
    public required bool Filled { get; init; }

    /// <summary>是否是最高档(难度 6)—— 满档时比普通已通关档更亮一档。</summary>
    public required bool IsMaxTier { get; init; }

    /// <summary>该档的序号(1..6),供悬停提示与无障碍文案使用。</summary>
    public required int Tier { get; init; }

    /// <summary>悬停提示,如 "难度 5"。</summary>
    public required string Tooltip { get; init; }
}

/// <summary>全息战略-地区菜单项(演武/同步/幻痛/强袭四个 logo)。</summary>
public sealed partial class HologramRegionItem : CommunityToolkit.Mvvm.ComponentModel.ObservableObject
{
    public required int CountryId { get; init; }
    public required string Name { get; init; }

    /// <summary>地区 logo(接口唯一以完整 URL 下发的图标字段)。</summary>
    public string? LogoUrl { get; init; }

    /// <summary>该地区 boss 总数。</summary>
    public required int BossCount { get; init; }

    /// <summary>该地区已通关任意档位的 boss 数(即"打过的"),菜单进度用。</summary>
    public required int ChallengedCount { get; init; }

    /// <summary>该地区已满档(难度 6 通关)的 boss 数(总览统计用)。</summary>
    public required int ClearedCount { get; init; }

    /// <summary>菜单上的进度文案,如 "10/10"(已打过 / 总数)。</summary>
    public required string ProgressText { get; init; }

    /// <summary>菜单进度条的百分比(0..100)。</summary>
    public required double ProgressPercent { get; init; }

    /// <summary>该地区是否含 boss(无 boss 的地区在菜单里弱化显示)。</summary>
    public required bool HasBosses { get; init; }

    /// <summary>是否为当前选中地区(由 VM 在切换时维护)。</summary>
    [ObservableProperty]
    private bool _isSelected;

    /// <summary>该地区的 boss 列表(选中地区时喂给右栏网格)。</summary>
    public required List<HologramBossItem> Bosses { get; init; }

    /// <summary>菜单项上的悬停提示(地区名 · 已通关进度 · 满档数),走本地化。</summary>
    public string TooltipText => LanguageService.Format(
        "Tower.Holo.RegionTooltip", Name, ProgressText, ClearedCount, BossCount);
}

/// <summary>全息战略-boss 卡片(地区网格里的一格)。</summary>
public sealed partial class HologramBossItem : CommunityToolkit.Mvvm.ComponentModel.ObservableObject
{
    public required int BossId { get; init; }
    public required string BossName { get; init; }

    /// <summary>方形头像(接口 bossHeadIcon)。</summary>
    public string? HeadIcon { get; init; }

    /// <summary>大图立绘(接口 bossIconUrl,详情 hero 用)。</summary>
    public string? HeroIcon { get; init; }

    /// <summary>已通关最高难度 0..6(0 = 未挑战)。</summary>
    public required int ClearedDifficulty { get; init; }

    /// <summary>难度文案,如 "难度 5/6";未挑战时为「未挑战」。</summary>
    public required string DifficultyText { get; init; }

    /// <summary>6 格难度刻度(已通关的档位上色)。</summary>
    public required List<HologramPip> Pips { get; init; }

    /// <summary>是否满档(难度 6 通关)。用于地区徽章的满档统计(悬停提示里给出),不再用于卡片描边。</summary>
    public required bool IsFullyCleared { get; init; }

    /// <summary>是否已通关任一档位(= 已挑战过)。未挑战时难度文案用次级灰,已挑战用常规文字色。</summary>
    public required bool HasAnyClear { get; init; }

    /// <summary>
    /// 该 boss 的逐档记录是否可用(= challengeDetails 是否成功拿到)。
    /// <para>
    /// 两个接口是独立抓取、各自 catch 的,所以"索引成功但详情失败"是一条必然可达的降级路径。
    /// 此时索引仍能告诉我们"打到第几档",但拿不到通关用时与配队 —— 必须把这个区别显式带出来,
    /// 否则右栏会把"记录拿不到"渲染成"未通关",与左边卡片上的「难度 N/6」自相矛盾。
    /// </para>
    /// </summary>
    public required bool RecordsAvailable { get; init; }

    /// <summary>是否为当前选中 boss(由 VM 在切换时维护)。</summary>
    [ObservableProperty]
    private bool _isSelected;

    /// <summary>该 boss 的全部难度记录(选中后按难度档位取用)。</summary>
    public required List<HologramChallengeRecord> Tiers { get; init; }
}

/// <summary>全息战略-详情里的通关角色(带本地属性图标)。</summary>
public sealed class HologramRoleItem
{
    public required string RoleName { get; init; }
    public required string LevelText { get; init; }
    public string? HeadIcon { get; init; }

    /// <summary>属性图标本地路径(Assets/attr/{natureId}.png;1..6 = 冷凝/热熔/导电/气动/衍射/湮灭)。</summary>
    public required string ElementIconPath { get; init; }

    /// <summary>属性名(图标 tooltip)。</summary>
    public required string ElementName { get; init; }
}

/// <summary>全息战略-详情里的难度档位(1..6)。</summary>
public sealed class HologramTierItem
{
    public required int Difficulty { get; init; }

    /// <summary>档位数字文案(界面上就是 1..6)。</summary>
    public required string Label { get; init; }

    /// <summary>该难度是否已通关(右侧带一个实心点表示)。</summary>
    public required bool IsCleared { get; init; }

    /// <summary>状态文案(「已通关」/「未通关」),让竖向列表每行自带说明。</summary>
    public required string StateText { get; init; }

    /// <summary>悬停提示,如 "难度 5(已通关)"。</summary>
    public required string Tooltip { get; init; }
}

/// <summary>
/// 深塔/海墟页:四个页签展示逆境深塔(towerDataDetail)、终焉矩阵(newTowerDetail)、
/// 再生海域(slashDetail)与全息战略(challengeIndex + challengeDetails),
/// 解析对齐 Java 版 WutheringWavesTool(TowerViewModel / NewTowerViewModel / SlashViewModel)。
/// </summary>
public sealed partial class TowerViewModel : ViewModelBase
{
    /// <summary>页签:0=逆境深塔,1=终焉矩阵,2=海墟,3=全息战略。</summary>
    [ObservableProperty]
    private int _selectedTabIndex;

    [ObservableProperty]
    private string _statusText = LanguageService.Format("Tower.NotLoaded");

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private bool _hasData;

    /// <summary>逆境深塔-已解锁(未解锁时页签内显示提示)。</summary>
    [ObservableProperty]
    private bool _towerUnlocked;

    /// <summary>逆境深塔-赛季刷新倒计时("X天Y小时后刷新",仅最高难度下显示)。</summary>
    [ObservableProperty]
    private string _towerSeasonEndText = "";

    /// <summary>终焉矩阵-已解锁(未解锁时页签内显示提示)。</summary>
    [ObservableProperty]
    private bool _newTowerUnlocked;

    /// <summary>海墟-刷新倒计时("X天Y小时后刷新",对齐 WutheringWavesTool updateSeasonEndTime)。</summary>
    [ObservableProperty]
    private string _slashSeasonEndText = "";

    /// <summary>海墟-再生海域总积分(allScore/maxScore,对齐 WutheringWavesTool score01)。</summary>
    [ObservableProperty]
    private string _slashTotalScoreText = "";

    /// <summary>海墟-无尽湍渊总积分(有记录才显示,对齐 WutheringWavesTool score02)。</summary>
    [ObservableProperty]
    private string _slashTurbidScoreText = "";

    /// <summary>是否显示无尽湍渊总积分。</summary>
    [ObservableProperty]
    private bool _slashHasTurbidScore;

    /// <summary>海墟-本期是否有挑战记录(无记录时页签内显示提示)。</summary>
    [ObservableProperty]
    private bool _slashHasRecord;

    // ---------------- 全息战略 ----------------

    /// <summary>全息战略-是否已解锁(未解锁时页签内显示提示)。</summary>
    [ObservableProperty]
    private bool _hologramUnlocked;

    /// <summary>全息战略-本期是否有任何 boss 数据(无数据时页签内显示提示)。</summary>
    [ObservableProperty]
    private bool _hologramHasData;

    /// <summary>
    /// 全息战略的两个接口是否至少有一个成功返回(决定该页签是否"有状态可展示")。
    /// <para>
    /// 与 <see cref="HologramHasData"/> 的区别:未解锁、或已解锁但没有 boss 时
    /// <c>HasData=false</c>,但页签仍需存在 —— 它要显示「尚未解锁」/「本期暂无数据」这两块兜底面板。
    /// 页面整体的 <c>HasData</c> 必须把它算进去,否则这些面板永远不可达。
    /// </para>
    /// </summary>
    [ObservableProperty]
    private bool _hologramLoaded;

    /// <summary>全息战略-总览统计文案,如 "已通关 18/23 个 boss"。</summary>
    [ObservableProperty]
    private string _hologramSummaryText = "";

    /// <summary>全息战略-四个地区 logo 菜单(演武/同步/幻痛/强袭)。</summary>
    public ObservableCollection<HologramRegionItem> HologramRegions { get; } = [];

    /// <summary>已解锁但接口未给出任何 boss(显示「本期暂无数据」占位)。</summary>
    public bool HologramShowEmpty => HologramUnlocked && !HologramHasData;

    partial void OnHologramUnlockedChanged(bool value) => OnPropertyChanged(nameof(HologramShowEmpty));

    partial void OnHologramHasDataChanged(bool value) => OnPropertyChanged(nameof(HologramShowEmpty));

    /// <summary>选择地区(四个 logo 菜单项)。</summary>
    [RelayCommand]
    private void SelectHologramRegion(HologramRegionItem? region)
    {
        if (region is not null)
        {
            SelectedHologramRegion = region;
        }
    }

    /// <summary>选择 boss(左栏卡片)。</summary>
    [RelayCommand]
    private void SelectHologramBoss(HologramBossItem? boss)
    {
        if (boss is not null)
        {
            SelectedHologramBoss = boss;
        }
    }

    /// <summary>当前选中地区。</summary>
    [ObservableProperty]
    private HologramRegionItem? _selectedHologramRegion;

    /// <summary>当前地区的 boss 卡片(选中地区时重建)。</summary>
    public ObservableCollection<HologramBossItem> HologramBosses { get; } = [];

    /// <summary>当前选中的 boss(选中后右栏显示其难度记录)。</summary>
    [ObservableProperty]
    private HologramBossItem? _selectedHologramBoss;

    /// <summary>当前查看的难度档位(1..6)。</summary>
    [ObservableProperty]
    private int _selectedHologramDifficulty = 1;

    /// <summary>难度档位选择器(1..6,含是否已通关)。</summary>
    public ObservableCollection<HologramTierItem> HologramTiers { get; } = [];

    /// <summary>
    /// 选择器当前选中的档位项(由 ListBox 双向绑定)。
    /// <para>用 SelectedItem 而不是让每个按钮自己发命令:选中态交给 ListBox 管,
    /// 换 boss 后重建集合也不会出现"选中项指向已消失的对象"。</para>
    /// </summary>
    [ObservableProperty]
    private HologramTierItem? _selectedHologramTier;

    /// <summary>当前 boss 当前难度的通关时间文案(如 "00:37");未通关为空串。</summary>
    [ObservableProperty]
    private string _hologramPassTimeText = "";

    /// <summary>当前档位是否已通关(决定显示通关时间还是「暂无挑战记录」)。</summary>
    [ObservableProperty]
    private bool _hologramTierCleared;

    /// <summary>
    /// 当前 boss 的逐档记录是否可用(challengeDetails 是否成功)。
    /// <para>false 时右栏显示「记录暂不可用」而非"未通关" —— 索引里明明写着打到第 N 档,
    /// 不能因为另一个接口失败就告诉用户"没打过"。</para>
    /// </summary>
    [ObservableProperty]
    private bool _hologramRecordsAvailable = true;

    /// <summary>记录可用且该档确实没打过 → 显示「暂无挑战记录」。记录不可用时不显示(另有提示)。</summary>
    public bool HologramShowNoRecord => HologramRecordsAvailable && !HologramTierCleared;

    partial void OnHologramRecordsAvailableChanged(bool value)
        => OnPropertyChanged(nameof(HologramShowNoRecord));

    partial void OnHologramTierClearedChanged(bool value)
        => OnPropertyChanged(nameof(HologramShowNoRecord));

    /// <summary>当前 boss 当前难度的 boss 等级文案,如 "等级 90"。</summary>
    [ObservableProperty]
    private string _hologramBossLevelText = "";

    /// <summary>当前 boss 名(详情标题;未选中时为空)。</summary>
    [ObservableProperty]
    private string _hologramBossName = "";

    /// <summary>
    /// 当前难度的立绘(接口每个难度一张独立 bossIconUrl,换档时 hero 图随之变化);
    /// 无记录时回退到索引里的 boss 大图。
    /// </summary>
    [ObservableProperty]
    private string? _hologramHeroIcon;

    /// <summary>当前档位的通关角色(最多 3 人;未通关时为空集合)。</summary>
    public ObservableCollection<HologramRoleItem> HologramRoles { get; } = [];

    /// <summary>
    /// 正在同步「boss ↔ 档位」关联,用于抑制 <see cref="OnSelectedHologramTierChanged"/> 的重入。
    /// <para>
    /// 换 boss 时「重建档位集合 + 改选中档位 + 刷新详情」必须按固定顺序发生;
    /// 三个入口(选中 boss / 选中档位 / 档位值变化)彼此会互相触发,
    /// 不加闸会出现"详情按旧档位渲染一次、又按新档位渲染一次"的抖动。
    /// </para>
    /// </summary>
    private bool _hologramSyncing;

    /// <summary>重建难度档位集合(1..6,标记各档是否已通关)。仅在换 boss 时调用。</summary>
    private void RebuildHologramTiers()
    {
        HologramTiers.Clear();
        foreach (var tier in BuildHologramTiers(SelectedHologramBoss))
        {
            HologramTiers.Add(tier);
        }
    }

    /// <summary>
    /// 构建某 boss 的难度档位列表(1..6,纯函数,供单测)。
    /// <para>
    /// 每档都产出「已通关/未通关」两个状态:<see cref="HologramTierItem.IsCleared"/> 给圆点,
    /// <see cref="HologramTierItem.StateText"/> 给竖向列表那一行的文案。
    /// 6 档恒定全出,即使某档没有记录 —— 缺行会让"打到第几档"读不出来。
    /// </para>
    /// <para>
    /// <b>降级路径</b>:challengeDetails 失败时 <see cref="HologramBossItem.Tiers"/> 为空,
    /// 但索引里的「已通关最高难度」仍然有效(已与详情交叉验证 23/23 一致)。
    /// 此时按索引回填档位状态,而不是把它们全渲染成「未通关」——
    /// 否则右边说"一档都没过"、左边卡片却写着「难度 5/6」,同一屏自相矛盾。
    /// </para>
    /// </summary>
    public static List<HologramTierItem> BuildHologramTiers(HologramBossItem? boss)
    {
        var tiers = boss?.Tiers ?? [];
        // 详情不可用时退回索引的"最高已通关难度"(两者实测一致,故回填是安全的)
        var useFallback = boss is { RecordsAvailable: false };
        var fallbackCleared = useFallback ? boss!.ClearedDifficulty : 0;
        var result = new List<HologramTierItem>(HologramParser.MaxDifficulty);
        for (var d = 1; d <= HologramParser.MaxDifficulty; d++)
        {
            var cleared = useFallback
                ? d <= fallbackCleared
                : HologramParser.IsCleared(tiers.FirstOrDefault(r => r.Difficulty == d));
            var state = LanguageService.Format(cleared ? "Tower.Holo.TierCleared" : "Tower.Holo.TierNotCleared");
            result.Add(new HologramTierItem
            {
                Difficulty = d,
                Label = $"{d}",
                IsCleared = cleared,
                StateText = state,
                Tooltip = LanguageService.Format("Tower.Holo.TierTooltip", d, state),
            });
        }
        return result;
    }

    /// <summary>
    /// 选中某 boss 时默认查看的难度(纯函数,供单测):停在其<b>已通关最高档</b>
    /// (那是用户最想先看的记录);没有记录则回到难度 1。
    /// </summary>
    public static int DefaultHologramDifficulty(HologramBossItem? boss)
        => boss is { ClearedDifficulty: > 0 } ? boss.ClearedDifficulty : 1;

    /// <summary>
    /// 在地区/地区内 boss 列表上打选中标记(供 XAML 的 <c>Classes.active</c> 绑定)。
    /// 用 VM 维护标记而不是在 XAML 里比较 Id:后者需要自定义多值转换器与父级 DataContext 查找,
    /// 在编译绑定下易碎且难测。
    /// </summary>
    private void SyncHologramSelection()
    {
        foreach (var region in HologramRegions)
        {
            region.IsSelected = ReferenceEquals(region, SelectedHologramRegion);
        }
        foreach (var boss in HologramBosses)
        {
            boss.IsSelected = ReferenceEquals(boss, SelectedHologramBoss);
        }
    }

    /// <summary>
    /// 写入选中的难度档位。
    /// <para>重入已由 <see cref="_hologramSyncing"/> 闸住,故直接走生成的属性(会正常发通知)。</para>
    /// </summary>
    private void SetHologramDifficultyCore(int difficulty) => SelectedHologramDifficulty = difficulty;

    partial void OnSelectedHologramRegionChanged(HologramRegionItem? value)
    {
        HologramBosses.Clear();
        if (value is not null)
        {
            foreach (var boss in value.Bosses)
            {
                HologramBosses.Add(boss);
            }
        }
        // 换地区后默认选中该地区第一个 boss(通常是最新 boss),让右栏不会是空的
        SelectedHologramBoss = HologramBosses.FirstOrDefault();
        SyncHologramSelection();
    }

    partial void OnSelectedHologramBossChanged(HologramBossItem? value)
    {
        if (_hologramSyncing)
        {
            return;
        }
        _hologramSyncing = true;
        try
        {
            // 换 boss 时把档位停在它的已通关最高档(无记录则回到难度 1)——
            // 这是用户最想先看的记录,而不是每次都从难度 1 重新点起
            var target = DefaultHologramDifficulty(value);
            RebuildHologramTiers();
            SetHologramDifficultyCore(target);
            _selectedHologramTier = HologramTiers.FirstOrDefault(t => t.Difficulty == target);
            OnPropertyChanged(nameof(SelectedHologramTier));
            RefreshHologramDetail();
        }
        finally
        {
            _hologramSyncing = false;
        }
        SyncHologramSelection();
    }

    partial void OnSelectedHologramTierChanged(HologramTierItem? value)
    {
        if (_hologramSyncing || value is null)
        {
            return;
        }
        _hologramSyncing = true;
        try
        {
            SetHologramDifficultyCore(value.Difficulty);
            RefreshHologramDetail();
        }
        finally
        {
            _hologramSyncing = false;
        }
    }

    /// <summary>
    /// 难度档位值被<b>外部</b>改动时(不经过 boss/tier 两个入口)也要把界面拉回一致。
    /// <para>
    /// <see cref="SelectedHologramDifficulty"/> 是公开可绑定的属性;没有这个钩子时,
    /// 谁直接赋值都会出现"档位值变了、但档位列表选中项与右栏详情还停在旧档"的错位
    /// (用户点一次才自愈)。这里补上:同步列表选中项 + 重绘详情。
    /// </para>
    /// </summary>
    partial void OnSelectedHologramDifficultyChanged(int value)
    {
        if (_hologramSyncing)
        {
            return;
        }
        _hologramSyncing = true;
        try
        {
            _selectedHologramTier = HologramTiers.FirstOrDefault(t => t.Difficulty == value);
            OnPropertyChanged(nameof(SelectedHologramTier));
            RefreshHologramDetail();
        }
        finally
        {
            _hologramSyncing = false;
        }
    }

    /// <summary>
    /// 按「当前 boss + 当前档位」刷新右栏详情(通关时间/等级/角色/立绘与档位提示)。
    /// <para>
    /// 只读渲染、不改集合结构:档位集合由 <see cref="RebuildHologramTiers"/> 在换 boss 时重建,
    /// 切档只更新这些标量属性。
    /// </para>
    /// </summary>
    private void RefreshHologramDetail()
    {
        var boss = SelectedHologramBoss;
        HologramRoles.Clear();
        HologramPassTimeText = "";
        HologramTierCleared = false;
        HologramBossLevelText = "";
        HologramBossName = boss?.BossName ?? "";
        HologramHeroIcon = boss?.HeroIcon;
        HologramRecordsAvailable = boss?.RecordsAvailable ?? true;

        if (boss is null)
        {
            return;
        }
        var current = boss.Tiers.FirstOrDefault(r => r.Difficulty == SelectedHologramDifficulty);
        // 记录不可用时不能声称"已通关"(拿不到 passTime/配队),也不能声称"未通关"
        // (索引说打到第 N 档了)—— 由 XAML 按 RecordsAvailable 显示"记录暂不可用"。
        HologramTierCleared = boss.RecordsAvailable && HologramParser.IsCleared(current);
        if (current is null)
        {
            return;
        }
        HologramBossLevelText = LanguageService.Format("Tower.Holo.BossLevel", current.BossLevel);
        // 每个难度有独立立绘:即便该档未通关也换成对应档位的图,让"翻档"有可见反馈
        HologramHeroIcon = string.IsNullOrWhiteSpace(current.BossIconUrl) ? boss.HeroIcon : current.BossIconUrl;
        if (!HologramTierCleared)
        {
            return;
        }
        HologramPassTimeText = HologramParser.FormatPassTime(current.PassTime);
        foreach (var role in (current.Roles ?? []).Take(3))
        {
            HologramRoles.Add(new HologramRoleItem
            {
                RoleName = role.RoleName ?? "",
                LevelText = LanguageService.Format("Tower.Holo.RoleLevel", role.RoleLevel),
                HeadIcon = role.RoleHeadIcon,
                ElementIconPath = ElementIconPathOf(role.NatureId),
                ElementName = ElementNameOf(role.NatureId),
            });
        }
    }

    /// <summary>属性图标本地路径(Assets/attr/{id}.png;与角色页同一份图标,1..6 冷凝/热熔/导电/气动/衍射/湮灭)。</summary>
    public static string ElementIconPathOf(int natureId)
        => natureId is >= 1 and <= 6
            ? Path.Combine(AppContext.BaseDirectory, "Assets", "attr", $"{natureId}.png")
            : "";

    /// <summary>属性名(图标 tooltip 用;未知 ID 返回空串)。</summary>
    public static string ElementNameOf(int natureId) => natureId switch
    {
        1 => LanguageService.Format("Tower.Holo.Element1"),
        2 => LanguageService.Format("Tower.Holo.Element2"),
        3 => LanguageService.Format("Tower.Holo.Element3"),
        4 => LanguageService.Format("Tower.Holo.Element4"),
        5 => LanguageService.Format("Tower.Holo.Element5"),
        6 => LanguageService.Format("Tower.Holo.Element6"),
        _ => "",
    };

    /// <summary>由全息战略两个接口的数据构建地区菜单(纯函数,供单测)。
    /// <para>
    /// 地区按接口 sort 降序(实测 演武→同步→幻痛→强袭,与界面四个 logo 顺序一致);
    /// 每个 boss 的「已通关最高难度」取索引里的 difficulty 字段
    /// (已与详情记录 <c>max(passTime&gt;0 的 difficulty)</c> 交叉验证 23/23 一致),6 格刻度按它填充。
    /// </para>
    /// </summary>
    public static List<HologramRegionItem> BuildHologramRegions(
        HologramIndexData? index, HologramDetailData? details)
    {
        var result = new List<HologramRegionItem>();
        foreach (var group in HologramParser.SortCountries(index?.ChallengeList))
        {
            var bosses = HologramParser.SortBosses(group.IndexList)
                .Select(b => BuildHologramBoss(b, details))
                .ToList();
            var challenged = bosses.Count(b => b.HasAnyClear);
            var cleared = bosses.Count(b => b.IsFullyCleared);
            result.Add(new HologramRegionItem
            {
                CountryId = group.Country!.CountryId,
                Name = group.Country.CountryName ?? "",
                LogoUrl = group.Country.HomePageImage,
                BossCount = bosses.Count,
                ChallengedCount = challenged,
                ClearedCount = cleared,
                ProgressText = LanguageService.Format("Tower.Holo.RegionProgress", challenged, bosses.Count),
                ProgressPercent = bosses.Count > 0 ? challenged * 100.0 / bosses.Count : 0,
                HasBosses = bosses.Count > 0,
                Bosses = bosses,
            });
        }
        return result;
    }

    /// <summary>由索引项 + 详情构建单个 boss 卡片(纯函数,供单测)。</summary>
    private static HologramBossItem BuildHologramBoss(HologramBossEntry entry, HologramDetailData? details)
    {
        var tiers = HologramParser.TiersOf(details, entry.BossId);
        var clearedDifficulty = Math.Clamp(entry.ClearedDifficulty, 0, HologramParser.MaxDifficulty);
        var fullyCleared = HologramParser.IsFullyCleared(clearedDifficulty);
        return new HologramBossItem
        {
            BossId = entry.BossId,
            BossName = entry.BossName ?? "",
            HeadIcon = entry.BossHeadIcon,
            HeroIcon = entry.BossIconUrl,
            ClearedDifficulty = clearedDifficulty,
            DifficultyText = clearedDifficulty > 0
                ? LanguageService.Format("Tower.Holo.DifficultyOf", clearedDifficulty, HologramParser.MaxDifficulty)
                : LanguageService.Format("Tower.Holo.NotChallenged"),
            Pips = [.. Enumerable.Range(1, HologramParser.MaxDifficulty)
                .Select(d => new HologramPip
                {
                    Tier = d,
                    Filled = d <= clearedDifficulty,
                    IsMaxTier = d == HologramParser.MaxDifficulty,
                    Tooltip = LanguageService.Format("Tower.Holo.PipTooltip", d),
                })],
            IsFullyCleared = fullyCleared,
            HasAnyClear = clearedDifficulty > 0,
            // 记录条数 > 0 才算"详情可用"(该 boss 在该接口里确实有档位数据)
            RecordsAvailable = tiers.Count > 0,
            Tiers = tiers,
        };
    }

    public ObservableCollection<TowerDifficultyItem> TowerDifficulties { get; } = [];

    /// <summary>当前选中难度的区域列表。</summary>
    public ObservableCollection<TowerAreaItem> TowerAreas { get; } = [];

    /// <summary>终焉矩阵模式列表。</summary>
    public ObservableCollection<TowerModeItem> TowerModes { get; } = [];

    /// <summary>终焉矩阵往期历史列表(按赛季结束时间降序)。</summary>
    public ObservableCollection<NewTowerHistoryItem> NewTowerHistory { get; } = [];

    /// <summary>是否有往期历史(控制「暂无往期记录」占位显隐)。</summary>
    public bool HasNewTowerHistory => NewTowerHistory.Count > 0;

    /// <summary>当前选中的矩阵模式(右栏详情)。</summary>
    [ObservableProperty]
    private TowerModeItem? _selectedTowerMode;

    /// <summary>当前选中的往期历史(与模式选择互斥)。</summary>
    [ObservableProperty]
    private NewTowerHistoryItem? _selectedNewTowerHistory;

    /// <summary>历史详情标题(如 "历史-终焉矩阵")。</summary>
    [ObservableProperty]
    private string _newTowerDetailTitle = "";

    /// <summary>历史详情(从本地库加载的一期记录)。</summary>
    [ObservableProperty]
    private TowerModeItem? _newTowerHistoryDetail;

    partial void OnSelectedTowerModeChanged(TowerModeItem? value)
    {
        if (value is not null)
        {
            _selectedNewTowerHistory = null;
            OnPropertyChanged(nameof(SelectedNewTowerHistory));
            NewTowerHistoryDetail = null;
            NewTowerDetailTitle = "";
        }
        OnPropertyChanged(nameof(NewTowerDetail));
        OnPropertyChanged(nameof(NewTowerShowWaiting));
    }

    partial void OnSelectedNewTowerHistoryChanged(NewTowerHistoryItem? value)
    {
        if (value is not null)
        {
            _selectedTowerMode = null;
            OnPropertyChanged(nameof(SelectedTowerMode));
            _ = LoadNewTowerHistoryDetailAsync(value);
        }
        else
        {
            NewTowerHistoryDetail = null;
            NewTowerDetailTitle = "";
        }
        OnPropertyChanged(nameof(NewTowerDetail));
        OnPropertyChanged(nameof(NewTowerShowWaiting));
    }

    partial void OnNewTowerHistoryDetailChanged(TowerModeItem? value)
    {
        OnPropertyChanged(nameof(NewTowerDetail));
        OnPropertyChanged(nameof(NewTowerShowWaiting));
    }

    /// <summary>右栏详情:历史查看优先,否则当前选中模式。</summary>
    public TowerModeItem? NewTowerDetail => NewTowerHistoryDetail ?? SelectedTowerMode;

    /// <summary>右栏是否显示「当前版本等待开放中」:本期无模式记录且未查看历史。</summary>
    public bool NewTowerShowWaiting => TowerModes.Count == 0 && NewTowerHistoryDetail is null;

    /// <summary>本次加载解析出的库街区角色条目 ID(矩阵历史按它落库/读取)。</summary>
    private string _newTowerRoleId = "";

    /// <summary>加载某期历史详情(对齐 WutheringWavesTool changHistory/updateHistoryData:取第一条模式记录)。</summary>
    private async Task LoadNewTowerHistoryDetailAsync(NewTowerHistoryItem item)
    {
        try
        {
            var json = await Task.Run(() => AppServices.Database.GetNewTowerHistory(_newTowerRoleId, item.EndTimeMillis));
            if (json is null)
            {
                NewTowerHistoryDetail = null;
                NewTowerDetailTitle = "";
                return;
            }
            var modes = JsonSerializer.Deserialize(json, TowerJsonContext.Default.ListNewTowerModeDetail);
            var first = modes?.FirstOrDefault();
            NewTowerDetailTitle = LanguageService.Format("Tower.HistoryMatrix");
            NewTowerHistoryDetail = first is null ? null : BuildModeItem(first, prefix: LanguageService.Format("Tower.HistoryPrefix"));
        }
        catch (Exception)
        {
            NewTowerHistoryDetail = null;
            NewTowerDetailTitle = "";
        }
        OnPropertyChanged(nameof(NewTowerShowWaiting));
    }

    /// <summary>
    /// 矩阵队伍按轮次分组(纯函数,供单测):轮次升序,每轮给出轮内合计积分与各队明细。
    /// 稳态协议(round=0/缺失)不显示轮次标题,归入同一组。
    /// </summary>
    public static List<TowerRoundItem> GroupTeamsByRound(IEnumerable<NewTowerTeam>? teams)
        => (teams ?? [])
            .GroupBy(t => t.Round)
            .OrderBy(g => g.Key)
            .Select(g => new TowerRoundItem
            {
                RoundText = g.Key > 0 ? LanguageService.Format("Tower.RoundNo", g.Key) : "",
                RoundScoreText = $"{g.Sum(t => t.Score)}",
                Teams = g.Select((t, i) => new TowerTeamItem
                {
                    TeamNoText = LanguageService.Format("Tower.TeamNo", i + 1),
                    ScoreText = $"{t.Score}",
                    PassText = $"{t.PassBoss}/{t.BossCount}",
                    Roles = t.RoleList ?? [],
                    Buffs = (t.Buffs ?? [])
                        .Select(b => new TowerBuffItem
                        {
                            BuffName = b.BuffName ?? LanguageService.Format("Tower.SpecialBuff"),
                            BuffIcon = b.BuffIcon,
                            BuffDescription = b.Desc,
                        }).ToList(),
                }).ToList(),
            })
            .ToList();

    /// <summary>把矩阵模式详情映射为展示项(rank 0-5 → C/B/A/S/SS/SSS,见 RankTextOf)。</summary>
    private static TowerModeItem BuildModeItem(NewTowerModeDetail m, string prefix = "")
    {
        var rank = m.Rank is >= 0 and <= 5 ? m.Rank : 0;
        var teams = m.Teams ?? [];
        // 按轮次分组(每轮给出轮内合计积分,便于对照"每轮队伍分数")
        var rounds = GroupTeamsByRound(teams);

        return new TowerModeItem
        {
            ModeName = prefix + (m.ModeId == 0 ? LanguageService.Format("Tower.ModeStable") : LanguageService.Format("Tower.ModeSingularity")),
            ScoreText = $"{m.Score}",
            ProgressText = m.ModeId == 0
                ? $"{m.PassBoss}/{m.BossCount}"
                : LanguageService.Format("Tower.RoundInfo", m.Round, m.PassBoss, m.BossCount),
            RankText = RankTextOf(rank),
            RankColor = RankToColor(rank),
            Roles = teams.SelectMany(t => t.RoleList ?? []).ToList(),
            Buffs = teams.SelectMany(t => t.Buffs ?? [])
                .Select(b => new TowerBuffItem
                {
                    BuffName = b.BuffName ?? LanguageService.Format("Tower.SpecialBuff"),
                    BuffIcon = b.BuffIcon,
                    BuffDescription = b.Desc,
                }).ToList(),
            Rounds = rounds,
        };
    }

    /// <summary>海墟关卡列表。</summary>
    public ObservableCollection<SlashChallengeItem> SlashChallenges { get; } = [];

    [ObservableProperty]
    private TowerDifficultyItem? _selectedTowerDifficulty;

    partial void OnSelectedTowerDifficultyChanged(TowerDifficultyItem? value)
    {
        TowerAreas.Clear();
        if (value is null)
        {
            TowerSeasonEndText = "";
            return;
        }
        foreach (var area in value.Areas)
        {
            TowerAreas.Add(area);
        }
        // 对齐 WutheringWavesTool TowerView:仅最高难度(difficulty==3)显示赛季刷新倒计时;
        // 若这一期已结束(库街区返回的是上一期数据),明确提示"本期已结束",而不是让倒计时凭空消失
        TowerSeasonEndText = value.ShowSeasonEnd ? SeasonEndText(_towerSeasonEndMillis) : "";
    }

    private long? _towerSeasonEndMillis;

    /// <summary>进入页面自动刷新深塔/海墟数据(替代原手动"刷新"按钮)。</summary>
    public TowerViewModel()
    {
        NewTowerHistory.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasNewTowerHistory));
        _ = LoadAsync();
    }

    /// <summary>上次成功/尝试拉取的时间(进入页面时的"新鲜度"判断,避免反复切页狂刷接口)。</summary>
    private DateTime _lastLoadAt = DateTime.MinValue;

    /// <summary>进入页面的最小重新拉取间隔(秒):越快于它反复切页就不重复请求,规避风控。</summary>
    private const int ReloadMinIntervalSeconds = 60;

    /// <summary>
    /// 导航到深塔/海墟页时调用:重新拉取数据。
    /// 页面 VM 在 App 启动时就全部建好并只构造一次(见 MainWindowViewModel),构造函数里的
    /// 那次加载发生在启动瞬间;没有这个入口,页面上永远是启动那一刻的战绩快照
    /// —— 这正是"深塔海墟数据不刷新"的原因(游戏内打完再进来还是旧分,只能重启 App)。
    /// </summary>
    public void OnNavigatedTo()
    {
        if (IsBusy)
        {
            return;
        }
        // 刚拉过就不重复拉(切页很频繁;接口还有风控风险)
        if (DateTime.Now - _lastLoadAt < TimeSpan.FromSeconds(ReloadMinIntervalSeconds))
        {
            return;
        }
        _ = LoadAsync();
    }

    [RelayCommand]
    private async Task LoadAsync()
    {
        if (IsBusy)
        {
            return;
        }
        IsBusy = true;
        _lastLoadAt = DateTime.Now;
        StatusText = LanguageService.Format("Common.Loading");
        try
        {
            var data = await AppServices.Tower.GetTowerDataAsync();
            var (tower, newTower, slash, error, roleId) =
                (data.Tower, data.NewTower, data.Slash, data.Error, data.RoleId);
            TowerDifficulties.Clear();
            TowerAreas.Clear();
            TowerModes.Clear();
            SlashChallenges.Clear();
            SlashSeasonEndText = "";
            SlashTotalScoreText = "";
            SlashTurbidScoreText = "";
            SlashHasTurbidScore = false;
            SlashHasRecord = false;
            SelectedTowerMode = null;
            SelectedNewTowerHistory = null;
            NewTowerHistoryDetail = null;
            NewTowerDetailTitle = "";
            NewTowerHistory.Clear();
            HologramRegions.Clear();
            HologramBosses.Clear();
            HologramTiers.Clear();
            HologramRoles.Clear();
            SelectedHologramRegion = null;
            SelectedHologramBoss = null;
            SelectedHologramDifficulty = 1;
            HologramUnlocked = false;
            HologramHasData = false;
            HologramLoaded = false;
            HologramSummaryText = "";
            HologramPassTimeText = "";
            HologramTierCleared = false;
            HologramBossLevelText = "";
            HologramBossName = "";
            HologramHeroIcon = null;
            if (!string.IsNullOrEmpty(error))
            {
                StatusText = error;
                HasData = false;
                return;
            }

            // ---- 逆境深塔(towerDataDetail,对齐 TowerDataDetailTask/TowerViewModel) ----
            if (tower is not null)
            {
                TowerUnlocked = tower.IsUnlock;
                _towerSeasonEndMillis = tower.SeasonEndTime;
                var sorted = TowerSeasonParser.SortDifficulties(tower.DifficultyList);
                if (sorted.Count > 0)
                {
                    // 每难度的区域/楼层映射为展示项
                    var items = sorted
                        .Where(d => (d.TowerAreaList?.Count ?? 0) > 0)
                        .Select(d => new TowerDifficultyItem
                        {
                            DifficultyName = d.DifficultyName ?? LanguageService.Format("Tower.Difficulty", d.Difficulty),
                            Difficulty = d.Difficulty,
                            Areas = d.TowerAreaList!.Select(a => new TowerAreaItem
                            {
                                AreaName = a.AreaName ?? LanguageService.Format("Tower.Area", a.AreaId),
                                StarText = $"{a.Star} / {a.MaxStar}",
                                Floors = (a.FloorList ?? [])
                                    .OrderBy(f => f.Floor)
                                    .Select(f => new TowerFloorItem
                                    {
                                        FloorName = LanguageService.Format("Tower.Floor", f.Floor),
                                        Star = Math.Max(0, Math.Min(f.Star, 3)),
                                        Roles = f.RoleList ?? [],
                                    })
                                    .ToList(),
                            }).ToList(),
                        })
                        .ToList();
                    foreach (var d in items)
                    {
                        TowerDifficulties.Add(d);
                    }
                    SelectedTowerDifficulty = TowerDifficulties.FirstOrDefault();
                }
            }

            // ---- 终焉矩阵(newTowerDetail,对齐 NewTowerViewModel:modeId 0=稳态协议,其余=奇点扩张) ----
            if (newTower is not null)
            {
                NewTowerUnlocked = newTower.IsUnlock;
                // 本期已结束时(endTime ≤ 0),接口返回的是**上一期**的战绩:这批数据已由
                // TowerService.SaveNewTowerHistory 落库为历史一期,不应再挂在「挑战模式」(本期)下,
                // 否则同一期数据同时出现在"本期"和"往期历史"两处,看起来像本期已出成绩。
                var newTowerEnded = TowerSeasonParser.IsSeasonEnded(newTower.EndTime);
                if (!newTowerEnded)
                {
                    foreach (var m in newTower.ModeDetails?.Where(x => x.HasRecord && x.Score > 0) ?? [])
                    {
                        TowerModes.Add(BuildModeItem(m));
                    }
                    SelectedTowerMode = TowerModes.FirstOrDefault();
                }
            }

            // 往期历史(本地库,按赛季结束时间降序;对齐 WutheringWavesTool initHistory)
            if (!string.IsNullOrEmpty(roleId))
            {
                // 必须记录本次 roleId:LoadNewTowerHistoryDetailAsync 用它读历史详情。
                // 此前该字段从未赋值(恒为空串)→ 读库必然查不到行 → 点「往期历史」右栏
                // 永远停在"当前版本等待开放中",表现为"上期记录没有显示"。
                _newTowerRoleId = roleId;
                foreach (var end in AppServices.Database.GetNewTowerHistoryEndTimes(roleId))
                {
                    var endLocal = DateTimeOffset.FromUnixTimeMilliseconds(end).LocalDateTime;
                    // 只显示一个孤立截止日时看不出"这一期覆盖哪段时间";按赛季周期倒推起始日,
                    // 与参考实现(起止日期两行 + 时间节点)一致。
                    var startLocal = TowerSeasonParser.SeasonStartDate(endLocal);
                    NewTowerHistory.Add(new NewTowerHistoryItem
                    {
                        EndTimeMillis = end,
                        Label = LanguageService.Format("Tower.RecordBefore", endLocal.ToString("yyyy.MM.dd")),
                        StartDateText = startLocal.ToString("yyyy.MM.dd"),
                        EndDateText = endLocal.ToString("yyyy.MM.dd"),
                    });
                }
                // 本期无成绩但有往期记录时,默认展开最近一期:否则右栏一直显示空态,
                // 用户会以为"往期历史是坏的"。本期有成绩时不抢占,保持"本期优先"。
                if (TowerModes.Count == 0 && NewTowerHistory.Count > 0)
                {
                    SelectedNewTowerHistory = NewTowerHistory[0];
                }
            }

            // ---- 海墟(slashDetail,对齐 SlashViewModel.updateDate/updateScore) ----
            if (slash is { DifficultyList: not null })
            {
                // difficulty:1=再生海域-海隙,2=无尽湍渊;0=禁忌海域不计
                var regen = slash.DifficultyList.FirstOrDefault(d => d.Difficulty == 1);
                var turbid = slash.DifficultyList.FirstOrDefault(d => d.Difficulty == 2);
                SlashSeasonEndText = SeasonEndText(slash.SeasonEndTime);

                // 1. 过滤 difficulty==0(禁忌海域)与 allScore==0
                // 2. 无尽湍渊(difficulty=2)的关卡插到「再生海域」(difficulty=1)列表头
                // 3. 赛季已结束时接口返回的是上一期残留:只保留跨赛季延续的第 7、8 关,
                //    丢弃每期清零的 9/10/11 与无尽湍渊(否则会拿上赛季满档成绩充当本期成绩)
                var seasonEnded = TowerSeasonParser.IsSeasonEnded(slash.SeasonEndTime);
                var selected = TowerSeasonParser.SelectSlashChallenges(slash.DifficultyList, seasonEnded);

                // 总积分:赛季已结束时不能沿用接口的整季合计(含已清零的 9/10/11),
                // 否则会把上赛季的 19220 当成"本期总积分";改按实际展示的关卡求和 ——
                // 7/8 跨赛季延续计为已有成绩,每期重打的关卡本期为 0。
                if (seasonEnded)
                {
                    var regenShown = selected.Where(s => !s.IsTurbid).Sum(s => s.Challenge.Score);
                    if (regen is not null && regenShown > 0)
                    {
                        SlashTotalScoreText = $"{regenShown} / {regen.MaxScore}";
                    }
                }
                else
                {
                    if (regen is not null && regen.AllScore > 0)
                    {
                        SlashTotalScoreText = $"{regen.AllScore} / {regen.MaxScore}";
                    }
                    if (turbid is not null && turbid.AllScore > 0)
                    {
                        SlashTurbidScoreText = LanguageService.Format("Tower.TurbidScore", turbid.AllScore, turbid.MaxScore);
                        SlashHasTurbidScore = true;
                    }
                }

                foreach (var (c, isTurbid) in selected)
                {
                    var halves = c.HalfList!;
                    SlashChallenges.Add(new SlashChallengeItem
                    {
                        ChallengeId = c.ChallengeId,
                        ChallengeNoText = LanguageService.Format("Tower.ChallengeNo", c.ChallengeId),
                        ChallengeName = isTurbid
                            ? (c.ChallengeName ?? LanguageService.Format("Tower.TurbidName", c.ChallengeId))
                            : (c.ChallengeName ?? LanguageService.Format("Tower.LevelName", c.ChallengeId)),
                        ScoreText = $"{c.Score}",
                        RankText = SlashRankText(c.Rank),
                        RankColor = SlashRankColor(c.Rank),
                        Teams = halves.Select((h, i) => new SlashTeamItem
                        {
                            TeamName = i == 0 ? LanguageService.Format("Tower.TeamFirst") : LanguageService.Format("Tower.TeamSecond"),
                            ScoreText = $"{h.Score}",
                            BuffName = string.IsNullOrWhiteSpace(h.BuffName) ? LanguageService.Format("Tower.NoBuff") : h.BuffName!,
                            BuffIcon = h.BuffIcon,
                            BuffDescription = h.BuffDescription,
                            Roles = h.RoleList ?? [],
                        }).ToList(),
                    });
                }
                SlashHasRecord = SlashChallenges.Count > 0;
            }

            // ---- 全息战略(challengeIndex + challengeDetails) ----
            if (data.HologramIndex is not null || data.HologramDetails is not null)
            {
                // 两个接口的 isUnlock 任一为 true 即视为解锁(空数据时该键整体缺失 → 保持 false,
                // 页面显示「尚未解锁」;实测无 roleId 时接口返回 200 + 空 challengeList)
                HologramUnlocked = data.HologramIndex?.IsUnlock == true || data.HologramDetails?.IsUnlock == true;
                HologramLoaded = true;
                foreach (var region in BuildHologramRegions(data.HologramIndex, data.HologramDetails))
                {
                    HologramRegions.Add(region);
                }
                HologramHasData = HologramRegions.Any(r => r.HasBosses);
                // 总览与地区徽章同一口径(已打过 / 总数),不再单独讲「满档」——
                // 满档数仍在悬停提示里给需要的人查
                var totalBosses = HologramRegions.Sum(r => r.BossCount);
                var totalChallenged = HologramRegions.Sum(r => r.ChallengedCount);
                HologramSummaryText = totalBosses > 0
                    ? LanguageService.Format("Tower.Holo.Summary", totalChallenged, totalBosses)
                    : "";
                // 默认选中第一个有 boss 的地区(该地区按 sort 最新),右栏立即有内容
                SelectedHologramRegion = HologramRegions.FirstOrDefault(r => r.HasBosses);
            }

            HasData = TowerDifficulties.Count > 0 || TowerModes.Count > 0
                || SlashChallenges.Count > 0 || HologramHasData;
            // 全息页签自己一旦有"可展示的状态"(解锁与否/有无 boss)就撑起整页 TabControl:
            // 否则「尚未解锁」「本期暂无数据」这两块专门写的兜底面板永远显示不出来 ——
            // HasData 为 false 时连 TabControl 一起隐藏,用户只会看到通用提示
            // (实测无效路径:账号无深塔/矩阵/海墟数据时)。
            HasData = HasData || HologramLoaded;
            StatusText = LanguageService.Format(
                "Tower.StatusLoaded", TowerDifficulties.Count, TowerModes.Count,
                SlashChallenges.Count, HologramRegions.Sum(r => r.BossCount));
        }
        catch (Exception ex)
        {
            StatusText = LanguageService.Format("Status.LoadFailedWith", ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// 赛季剩余时间文案:正常 → "X天Y小时后刷新";≤0(接口返回的这一期已结束)→ "本期已结束"。
    /// 后者是"看起来数据没刷新"的常见成因:账号没打新一期时接口会把上一期数据连着已过去的
    /// seasonEndTime 一起返回,旧实现此时返回空串,倒计时消失但分数照旧显示。
    /// </summary>
    private static string SeasonEndText(long? remainingMillis)
        => TowerSeasonParser.IsSeasonEnded(remainingMillis)
            ? LanguageService.Format("Tower.SeasonEnded")
            : TowerSeasonParser.RefreshText(remainingMillis);

    /// <summary>
    /// 矩阵评级 rank → 字母(公开供单测)。实机数据 rank 值域 0..5(稳态协议 3=S、奇点扩张 44065 分 rank=5),
    /// 参考实现只有 4 级 {"C","B","A","S"},rank≥4 会落到范围外被显示成 C(实测 44065 分被标成 C),
    /// 故按库街区 6 级补全:sss/ss/s/a/b/c。
    /// </summary>
    public static string RankTextOf(int rank) => rank switch
    {
        0 => "C",
        1 => "B",
        2 => "A",
        3 => "S",
        4 => "SS",
        5 => "SSS",
        _ => "?",
    };

    /// <summary>数值档评级色:先转文字档,再走与 <see cref="SlashRankColor"/> 同一份档位→色值表
    /// (评审反馈:此前 4/5 仍落 S 的黄色,同一页两套顶级档配色不一致)。</summary>
    private static string RankToColor(int rank) => SlashRankColor(RankTextOf(rank));

    /// <summary>
    /// 海墟 rank 是字符串(S/A/B/C/SS/SSS),直接展示(参照 WutheringWavesTool SlashChallenge.rank)。
    /// 第 12 关「无尽湍渊」满档为 SSS,SSS 红 / SS 橙突出顶级档;S/A/B/C 沿用原配色。
    /// </summary>
    private static string SlashRankText(string? rank)
        => string.IsNullOrWhiteSpace(rank) ? "?" : rank;

    /// <summary>
    /// 海墟评级徽章底色(公开供单测):SSS 红 / SS 橙 / S 黄 / A 蓝 / B 绿 / C 与未知灰。
    /// 第 12 关「无尽湍渊」满档为 SSS,红色突出顶级档。
    /// </summary>
    public static string SlashRankColor(string? rank) => (rank ?? "").ToUpperInvariant() switch
    {
        "SSS" => "#e33737",   // 满档 SSS:红(参照角色页声骸 ACE 红档)
        "SS" => "#ff9800",    // 次满档 SS:橙
        "S" => "#f8f05c",
        "A" => "#2196f3",
        "B" => "#4caf50",
        "C" => "#9e9e9e",
        _ => "#9e9e9e",
    };
}
