using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using McKuro.Core.Models.Tower;
using McKuro.Services;

namespace McKuro.ViewModels;

/// <summary>地图探索-地区(一级)数据项:国家名、主图、总探索度与它下面的区域。</summary>
public sealed class ExploreCountryItem
{
    public required int CountryId { get; init; }

    public required string Name { get; init; }

    /// <summary>
    /// 地区主图(左栏一级项的缩略图)。接口有多套图片字段,解析见
    /// <see cref="ExploreParser.ResolveImageUrl"/>;优先 1.63:1 的「主页图片」——
    /// 正方形图标压到 44px 缩略图上会把图形挤没,极宽的「名片图」裁成方块又只剩一条色带。
    /// </summary>
    public string? MainImageUrl { get; init; }

    /// <summary>主图是否可用(不可用时不放 <c>AsyncImage</c>,底板本身仍在)。</summary>
    public bool HasMainImage => !string.IsNullOrWhiteSpace(MainImageUrl);

    /// <summary>该国总探索度(0..100,仅用于画进度条)。</summary>
    public required double ProgressPercent { get; init; }

    /// <summary>总探索度文案,如 "45.2%";接口没给数值时为 "--"。</summary>
    public required string ProgressText { get; init; }

    /// <summary>该国全部区域(接口顺序)。</summary>
    public required List<ExploreAreaItem> Areas { get; init; }

    /// <summary>区域数文案,如 "12 个区域"。</summary>
    public required string AreaCountText { get; init; }

    /// <summary>子地区(今州/梦州/拉海洛…);接口没有这个层级,由 <see cref="ExploreRegionCatalog"/> 归类。</summary>
    public required List<ExploreSubRegionItem> SubRegions { get; init; }
}

/// <summary>地图探索-子地区(二级)数据项。</summary>
public sealed class ExploreSubRegionItem
{
    public required string Key { get; init; }

    public required string Name { get; init; }

    public required List<ExploreAreaItem> Areas { get; init; }

    public required string AreaCountText { get; init; }

    /// <summary>
    /// 子地区的探索度:接口只给「国家总探索度」和「区域探索度」,没有子地区这一级,
    /// 故取该子地区各区域探索度的**算术平均**(区域之间同等权重)——
    /// 界面上标为推算值,不要当成官方数字。
    /// </summary>
    public required double ProgressPercent { get; init; }

    public required string ProgressText { get; init; }
}

/// <summary>
/// 地图探索-左栏菜单项(一级地区或二级子地区)。
/// <para>
/// 左栏把两级拍平成一条列表(一级项带主图,二级项缩进 + 竖线标记),
/// 比嵌套 <c>TreeView</c> 更可控:选中态、悬停态、缩进都走同一套样式。
/// </para>
/// </summary>
public sealed partial class ExploreNodeItem : ObservableObject
{
    public required int CountryId { get; init; }

    public required string Name { get; init; }

    /// <summary>一级项:地区主图;二级项为空。</summary>
    public string? ImageUrl { get; init; }

    public bool HasImage => !string.IsNullOrWhiteSpace(ImageUrl);

    /// <summary>二级项:缩进 + 竖线标记,并且不再显示主图。</summary>
    public required bool IsSubRegion { get; init; }

    /// <summary>二级项所属的一级地区 id(一级项为 0)。折叠时靠它找到父项。</summary>
    public required int ParentCountryId { get; init; }

    /// <summary>是否带二级地区(= 该地区配置了子地区)。只有一级项可能为 true。</summary>
    public required bool HasChildren { get; init; }

    /// <summary>二级地区是否展开(默认展开:一进页面就能看到分级)。</summary>
    [ObservableProperty]
    private bool _isExpanded = true;

    public required double ProgressPercent { get; init; }

    public required string ProgressText { get; init; }

    public required string AreaCountText { get; init; }

    /// <summary>选中时要展示的区域(一级 = 该地区全部区域,二级 = 该子地区的区域)。</summary>
    public required List<ExploreAreaItem> Areas { get; init; }

    /// <summary>右栏标题:二级项带上级地区名(「瑝珑 · 今州」),一级项就是地区名。</summary>
    public required string HeaderText { get; init; }

    [ObservableProperty]
    private bool _isSelected;

    /// <summary>悬停提示(名称 · 探索度 · 区域数)。</summary>
    public string TooltipText => LanguageService.Format(
        "Tower.Explore.CountryTooltip", HeaderText, ProgressText, AreaCountText);
}

/// <summary>地图探索-区域(地图)展示项。</summary>
public sealed class ExploreAreaItem
{
    public required int AreaId { get; init; }

    public required string Name { get; init; }

    /// <summary>区域预览图(接口偶发给目录前缀,已在解析时过滤)。</summary>
    public string? PicUrl { get; init; }

    public bool HasPic => !string.IsNullOrWhiteSpace(PicUrl);

    /// <summary>区域探索度(0..100)。</summary>
    public required double ProgressPercent { get; init; }

    /// <summary>区域探索度文案,如 "87.5%"。</summary>
    public required string ProgressText { get; init; }

    /// <summary>该区域的探索物进度(奇藏/信标/声匣…)。</summary>
    public required List<ExploreItemProgressItem> Items { get; init; }

    public bool HasItems => Items.Count > 0;
}

/// <summary>地图探索-探索物进度项(区域卡片里的小标签)。</summary>
public sealed class ExploreItemProgressItem
{
    public required string Name { get; init; }

    public string? IconUrl { get; init; }

    public bool HasIcon => !string.IsNullOrWhiteSpace(IconUrl);

    /// <summary>探索物进度文案,如 "60%"。</summary>
    public required string ProgressText { get; init; }

    /// <summary>悬停提示,如 "奇藏 60%"。</summary>
    public string TooltipText => $"{Name} {ProgressText}";
}

/// <summary>
/// 地图探索-区域卡片的一行(固定两列)。
/// <para>
/// 为什么不直接用 <c>UniformGrid</c>:它的"uniform"是**整张网格同高**(取所有卡片里最高的那张),
/// 于是只要有一个区域有 10 个探索物(比如落渊南丘),所有行都会被撑到那个高度,
/// 上下两行卡片之间就会空出一大截。改成在 VM 里先按 2 个一行分好,行内两张卡等高、
/// 行高只由这一行决定,行间距就恒等于布局里写的 12px。
/// </para>
/// </summary>
public sealed class ExploreAreaRowItem
{
    /// <summary>本行的格子(长度 = 列数;区域不够时用 null 占位,保证列宽不被拉歪)。</summary>
    public required List<ExploreAreaItem?> Cells { get; init; }

    /// <summary>列数(随窗口宽度变化,见 <see cref="TowerViewModel.SetExploreAreaColumns"/>)。</summary>
    public required int Columns { get; init; }
}

/// <summary>
/// 深塔/海墟页的「地图探索度」页签(exploreIndex)。
/// <para>
/// 数据与深塔/海墟/全息走同一次 <c>TowerService.GetTowerDataAsync</c> 拉取(同一个 accessToken、
/// 同一套进入页面即刷新),这里只负责把接口结构映射成展示项 —— 与 <c>TowerViewModel.cs</c> 里
/// 全息战略的映射写法保持一致,但拆到单独文件,避免那个已经 1200 行的文件继续膨胀。
/// </para>
/// </summary>
public sealed partial class TowerViewModel
{
    /// <summary>地图探索-地区(一级数据,顺序即接口返回顺序,即官方 H5 的页签顺序)。</summary>
    public ObservableCollection<ExploreCountryItem> ExploreCountries { get; } = [];

    /// <summary>左栏菜单:一级地区 + 二级子地区拍平后的列表(只含"当前可见"的项)。</summary>
    public ObservableCollection<ExploreNodeItem> ExploreNodes { get; } = [];

    /// <summary>
    /// 全部菜单项(含被折叠隐藏的二级项)。<see cref="ExploreNodes"/> 是它按展开状态过滤后的视图 ——
    /// 折叠状态一变就重建视图,避免每个二级项各自去绑定父项的 IsExpanded(那样还得处理双向失效)。
    /// </summary>
    private readonly List<ExploreNodeItem> _nodeSource = [];

    /// <summary>当前选中项下的区域卡片(切换一级/二级项时重建)。</summary>
    public ObservableCollection<ExploreAreaItem> ExploreAreas { get; } = [];

    /// <summary>同上,但按"行"分好组 —— 界面用它渲染,行间距因此恒为 12px。</summary>
    public ObservableCollection<ExploreAreaRowItem> ExploreAreaRows { get; } = [];

    /// <summary>
    /// 区域卡片列数(2~6)。由视图按可用宽度回调设置:卡片目标宽 ~340px 时,
    /// 150px 高的通栏主图接近源图 2.1:1;窗口拉宽就多分列,而不是把卡片一并拉宽 ——
    /// 那样主图会被 UniformToFill 裁得只剩一条(全屏时"主图显示不完整"就是这个原因)。
    /// </summary>
    [ObservableProperty]
    private int _exploreAreaColumns = 2;

    /// <summary>区域卡片列数的合法范围(2~6):1 列会让主图被拉成横幅,超过 6 列每张卡窄到读不出数据。</summary>
    public static int ClampAreaColumns(int columns) => Math.Clamp(columns, 2, 6);

    /// <summary>视图在尺寸变化时回调:列数变了才重建行分组(相同则直接返回,避免每次滚动/重绘都重建)。</summary>
    public void SetExploreAreaColumns(int columns)
    {
        columns = ClampAreaColumns(columns);
        if (columns == ExploreAreaColumns)
        {
            return;
        }
        ExploreAreaColumns = columns;
        RebuildAreaRows();
    }

    private void RebuildAreaRows()
    {
        ExploreAreaRows.Clear();
        foreach (var row in BuildAreaRows(ExploreAreas, ExploreAreaColumns))
        {
            ExploreAreaRows.Add(row);
        }
    }

    /// <summary>当前选中的左栏项(一级地区或二级子地区)。</summary>
    [ObservableProperty]
    private ExploreNodeItem? _selectedExploreNode;

    /// <summary>接口是否成功返回过(区分「加载失败」与「确实没有探索记录」)。</summary>
    [ObservableProperty]
    private bool _exploreLoaded;

    /// <summary>是否解析出任何国家(有国家但没区域也算有内容)。</summary>
    [ObservableProperty]
    private bool _exploreHasData;

    /// <summary>总览文案,如 "共 4 个地区 · 51 个区域"。</summary>
    [ObservableProperty]
    private string _exploreSummaryText = "";

    /// <summary>接口成功但没有探索记录 → 显示「暂无探索数据」兜底面板。</summary>
    public bool ExploreShowEmpty => ExploreLoaded && !ExploreHasData;

    /// <summary>
    /// 接口没拿到数据(未登录/令牌失效/token 过期/风控)→ 显示「探索数据暂不可用」兜底面板。
    /// <para>
    /// 必须与 <see cref="ExploreShowEmpty"/> 分开:两者都给用户看"空面板"时,
    /// 前者是"你确实没探索过"(去游戏里跑一圈就好),后者是"这次没取到"(重进本页再试)——
    /// 合成一句话会把接口故障说成玩家的进度问题。
    /// </para>
    /// </summary>
    public bool ExploreShowUnavailable => !ExploreLoaded;

    partial void OnExploreLoadedChanged(bool value)
    {
        OnPropertyChanged(nameof(ExploreShowEmpty));
        OnPropertyChanged(nameof(ExploreShowUnavailable));
    }

    partial void OnExploreHasDataChanged(bool value) => OnPropertyChanged(nameof(ExploreShowEmpty));

    /// <summary>
    /// 点击左栏项:一级地区**同时**是展开/收起开关(用户口径:点一下展开二级地区,再点一下折叠)。
    /// <para>
    /// 收起时如果当前选中的正是它下面的二级项,<see cref="RefreshVisibleNodes"/> 会把选中回退到该地区本身 ——
    /// 否则左栏里没有任何高亮项,右栏却还在显示一个已经看不见的二级项的区域列表。
    /// </para>
    /// </summary>
    [RelayCommand]
    private void SelectExploreNode(ExploreNodeItem? node)
    {
        if (node is null)
        {
            return;
        }
        if (node.HasChildren)
        {
            node.IsExpanded = !node.IsExpanded;
            RefreshVisibleNodes();
        }
        SelectedExploreNode = node;
    }

    /// <summary>按各一级项的展开状态重建可见列表(折叠后的二级项不再出现在左栏)。</summary>
    private void RefreshVisibleNodes()
    {
        var visible = VisibleNodesOf(_nodeSource);
        ExploreNodes.Clear();
        foreach (var node in visible)
        {
            ExploreNodes.Add(node);
        }
        if (SelectedExploreNode is not null && !visible.Contains(SelectedExploreNode))
        {
            SelectedExploreNode = visible.FirstOrDefault(n => n.CountryId == SelectedExploreNode.CountryId && !n.IsSubRegion)
                ?? visible.FirstOrDefault();
        }
    }

    /// <summary>可见项过滤(纯函数,公开供单测):一级项恒在,二级项只在父一级项展开时出现。</summary>
    public static List<ExploreNodeItem> VisibleNodesOf(IEnumerable<ExploreNodeItem> nodes)
    {
        var all = nodes as IReadOnlyList<ExploreNodeItem> ?? nodes.ToList();
        var expanded = new HashSet<int>(all.Where(n => !n.IsSubRegion && n.IsExpanded).Select(n => n.CountryId));
        return all
            .Where(n => !n.IsSubRegion || expanded.Contains(n.ParentCountryId))
            .ToList();
    }

    partial void OnSelectedExploreNodeChanged(ExploreNodeItem? value)
    {
        // 必须遍历**全量**节点(_nodeSource)而不是可见的 ExploreNodes:
        // 折叠后被隐藏的二级项不在可见集合里,只清可见项会让它残留 IsSelected ——
        // 再次展开时父项和这个子项会同时高亮。
        foreach (var node in _nodeSource)
        {
            node.IsSelected = ReferenceEquals(node, value);
        }
        ExploreAreas.Clear();
        ExploreAreaRows.Clear();
        if (value is null)
        {
            return;
        }
        foreach (var area in value.Areas)
        {
            ExploreAreas.Add(area);
        }
        RebuildAreaRows();
    }

    /// <summary>按列数把区域切成行(公开供单测:分组错一位会让卡片错位)。</summary>
    public static List<ExploreAreaRowItem> BuildAreaRows(IReadOnlyList<ExploreAreaItem> areas, int columns = 2)
    {
        columns = ClampAreaColumns(columns);
        var rows = new List<ExploreAreaRowItem>((areas.Count + columns - 1) / columns);
        for (var start = 0; start < areas.Count; start += columns)
        {
            var cells = new List<ExploreAreaItem?>(columns);
            for (var offset = 0; offset < columns; offset++)
            {
                var index = start + offset;
                cells.Add(index < areas.Count ? areas[index] : null);
            }
            rows.Add(new ExploreAreaRowItem { Cells = cells, Columns = columns });
        }
        return rows;
    }

    /// <summary>拉取前清空(与其它页签的 Clear 序列放在一起,避免上一账号的数据串到新账号)。</summary>
    internal void ResetExplore()
    {
        ExploreCountries.Clear();
        _nodeSource.Clear();
        ExploreNodes.Clear();
        ExploreAreas.Clear();
        ExploreAreaRows.Clear();
        SelectedExploreNode = null;
        ExploreLoaded = false;
        ExploreHasData = false;
        ExploreSummaryText = "";
    }

    /// <summary>
    /// 映射 exploreIndex 结果。<paramref name="data"/> 为 null 表示接口失败:保持「无数据」,
    /// 但不清掉其它页签状态 —— 这个页签是附加的,它失败不能把深塔/海墟/全息一起拖黑。
    /// </summary>
    internal void ApplyExplore(ExploreIndexData? data)
    {
        ResetExplore();
        if (data is null)
        {
            return;
        }
        ExploreLoaded = true;

        foreach (var entry in data.ExploreList ?? [])
        {
            if (entry is null)
            {
                continue; // 服务端偶发 JSON null 空洞:不能让它把整页签打挂
            }
            ExploreCountries.Add(BuildExploreCountry(entry));
        }

        foreach (var node in BuildExploreNodes(ExploreCountries))
        {
            _nodeSource.Add(node);
        }
        foreach (var node in VisibleNodesOf(_nodeSource))
        {
            ExploreNodes.Add(node);
        }

        ExploreHasData = ExploreCountries.Count > 0;
        var areaCount = ExploreCountries.Sum(c => c.Areas.Count);
        ExploreSummaryText = ExploreHasData
            ? LanguageService.Format("Tower.Explore.Summary", ExploreCountries.Count, areaCount)
            : "";
        // 默认选中第一个"有区域"的菜单项:右栏立刻有内容,不必让用户先点一下
        SelectedExploreNode = ExploreNodes.FirstOrDefault(n => n.Areas.Count > 0)
            ?? ExploreNodes.FirstOrDefault();
    }
    /// <summary>单个国家的映射(公开供单测:图标字段与百分比口径最容易写错)。</summary>
    public static ExploreCountryItem BuildExploreCountry(ExploreCountryEntry entry)
    {
        var info = entry.Country;
        var countryId = info?.CountryId ?? 0;
        var areas = (entry.AreaInfoList ?? [])
            .Where(a => a is not null)
            .Select(BuildExploreArea)
            .ToList();
        // countryProgress 是**字符串**包裹的百分比;空串/异常值一律显示 "--" 而不是假的 0%
        var total = ExploreParser.ParsePercent(entry.CountryProgress);
        return new ExploreCountryItem
        {
            CountryId = countryId,
            Name = string.IsNullOrWhiteSpace(info?.CountryName)
                ? LanguageService.Format("Tower.Explore.UnknownCountry")
                : info!.CountryName!,
            // 主图:优先 1.63:1 的 home_image(240×147),其次正方形图标,最后才是极宽的「名片图」。
            // 历史上有字段是目录前缀 —— 过滤逻辑统一在 ResolveImageUrl 里。
            MainImageUrl = ExploreParser.ResolveImageUrl(
                info?.HomePageImage, info?.HomePageIcon, info?.DetailPageImage,
                info?.DetailPagePic, info?.DetailPageAreaPic),
            ProgressPercent = ExploreParser.ClampPercent(total ?? 0),
            ProgressText = total.HasValue ? ExploreParser.FormatPercent(total.Value) : "--",
            Areas = areas,
            AreaCountText = LanguageService.Format("Tower.Explore.AreaCount", areas.Count),
            SubRegions = BuildSubRegions(countryId, areas),
        };
    }

    /// <summary>
    /// 把区域按 <see cref="ExploreRegionCatalog"/> 归到子地区(今州/梦州/拉海洛/其他区域)。
    /// 该国家没有配置分级时返回空表 —— 左栏就只有一层,不做无意义的嵌套。
    /// </summary>
    private static List<ExploreSubRegionItem> BuildSubRegions(int countryId, List<ExploreAreaItem> areas)
    {
        if (!ExploreRegionCatalog.HasSubRegions(countryId))
        {
            return [];
        }
        var buckets = new Dictionary<string, List<ExploreAreaItem>>(StringComparer.Ordinal);
        foreach (var area in areas)
        {
            var key = ExploreRegionCatalog.SubRegionOf(countryId, area.AreaId)
                ?? ExploreRegionCatalog.OtherKey;
            if (!buckets.TryGetValue(key, out var bucket))
            {
                buckets[key] = bucket = [];
            }
            bucket.Add(area);
        }
        return buckets
            .OrderBy(kv => ExploreRegionCatalog.OrderOf(countryId, kv.Key))
            .Select(kv =>
            {
                // 子地区没有官方探索度:取各区域平均(界面上这一级只是"看进度"，不是统计口径)
                var average = kv.Value.Count == 0
                    ? 0
                    : kv.Value.Average(a => a.ProgressPercent);
                return new ExploreSubRegionItem
                {
                    Key = kv.Key,
                    Name = LanguageService.Format(ExploreRegionCatalog.LabelKeyOf(kv.Key)),
                    Areas = kv.Value,
                    AreaCountText = LanguageService.Format("Tower.Explore.AreaCount", kv.Value.Count),
                    ProgressPercent = ExploreParser.ClampPercent(average),
                    ProgressText = ExploreParser.FormatPercent(ExploreParser.ClampPercent(average)),
                };
            })
            .ToList();
    }

    /// <summary>
    /// 把「国家 → 子地区 → 区域」拍平成左栏菜单项:有子地区的国家先给一个"全部"项,再逐个子地区;
    /// 没有子地区的国家只给一项。公开供单测(分级是否正确是这次的核心口径)。
    /// </summary>
    public static List<ExploreNodeItem> BuildExploreNodes(IEnumerable<ExploreCountryItem> countries)
    {
        var nodes = new List<ExploreNodeItem>();
        foreach (var country in countries)
        {
            nodes.Add(new ExploreNodeItem
            {
                CountryId = country.CountryId,
                ParentCountryId = 0,
                Name = country.Name,
                ImageUrl = country.MainImageUrl,
                IsSubRegion = false,
                HasChildren = country.SubRegions.Count > 0,
                ProgressPercent = country.ProgressPercent,
                ProgressText = country.ProgressText,
                AreaCountText = country.AreaCountText,
                Areas = country.Areas,
                HeaderText = country.Name,
            });
            foreach (var sub in country.SubRegions)
            {
                nodes.Add(new ExploreNodeItem
                {
                    CountryId = country.CountryId,
                    ParentCountryId = country.CountryId,
                    Name = sub.Name,
                    ImageUrl = null,
                    IsSubRegion = true,
                    HasChildren = false,
                    ProgressPercent = sub.ProgressPercent,
                    ProgressText = sub.ProgressText,
                    AreaCountText = sub.AreaCountText,
                    Areas = sub.Areas,
                    HeaderText = LanguageService.Format("Tower.Explore.SubTitle", country.Name, sub.Name),
                });
            }
        }
        return nodes;
    }

    /// <summary>单个区域的映射(纯函数,公开供单测:两处百分比口径最容易写错)。</summary>
    public static ExploreAreaItem BuildExploreArea(ExploreArea area)
    {
        var items = (area.ItemList ?? [])
            .Where(i => i is not null)
            .Select(i => new ExploreItemProgressItem
            {
                Name = string.IsNullOrWhiteSpace(i.Name)
                    ? LanguageService.Format("Tower.Explore.UnknownItem")
                    : i.Name!,
                IconUrl = ExploreParser.ResolveImageUrl(i.Icon),
                ProgressText = ExploreParser.FormatPercent(ExploreParser.ClampPercent(i.Progress)),
            })
            .ToList();
        var progress = ExploreParser.ClampPercent(area.AreaProgress);
        return new ExploreAreaItem
        {
            AreaId = area.AreaId,
            Name = string.IsNullOrWhiteSpace(area.AreaName)
                ? LanguageService.Format("Tower.Explore.UnknownArea", area.AreaId)
                : area.AreaName!,
            PicUrl = ExploreParser.ResolveImageUrl(area.AreaPic),
            ProgressPercent = progress,
            ProgressText = ExploreParser.FormatPercent(progress),
            Items = items,
        };
    }
}
