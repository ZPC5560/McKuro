using System.Text.Json;
using McKuro.Core.Models.Tower;
using McKuro.Services;
using McKuro.ViewModels;
using Xunit;

namespace McKuro.Tests;

/// <summary>
/// 地图探索(exploreIndex)的模型解析与「数据 → 展示项」映射测试(纯函数部分,不依赖界面)。
/// <para>
/// 这个页签的坑全在口径上,故测试也照着口径写:
/// <list type="bullet">
/// <item><c>countryProgress</c> 是<b>字符串</b>包裹的百分比,<c>areaProgress</c>/<c>item.progress</c> 是数字 —— 三种读法。</item>
/// <item>国家图标字段可能是「目录前缀」(以 / 结尾),不能当图片地址用。</item>
/// <item>进度缺失时显示 "--" 而不是 0%(否则界面会告诉用户"探索度 0%",这是假消息)。</item>
/// </list>
/// </para>
/// </summary>
public class ExploreIndexTests
{
    /// <summary>贴近接口真实形态的样本:两个国家、区域若干、探索物若干,含一个目录前缀图标。</summary>
    private const string SampleJson =
        """
        {
          "open": true,
          "detectionInfoList": [
            { "acronym": "SQ", "detectionIcon": "https://img/det-1.png",
              "detectionId": 6000216, "detectionName": "朔雷之鳞", "level": 5, "levelName": "五星" }
          ],
          "exploreList": [
            {
              "country": {
                "countryId": 1, "countryName": "朔原",
                "bgColor": "#1b2436", "detailPageProgressColor": "#f8c94c",
                "homePageIcon": "https://web-static.kurobbs.com/icon/", "homePageImage": "https://img/hl.png",
                "detailPageImage": "https://img/hl-detail.png", "detailPagePic": "https://img/hl-pic.png",
                "detailPageAreaPic": "https://img/hl-area/"
              },
              "countryProgress": "68.40",
              "areaInfoList": [
                {
                  "areaId": 46, "areaName": "云陵谷", "areaPic": "https://img/area-46.png",
                  "areaProgress": 72.5,
                  "itemList": [
                    { "icon": "https://img/i-1.png", "name": "奇藏", "progress": 80.0, "type": 1 },
                    { "icon": "Config/33/project_icon/beacon.png", "name": "信标", "progress": 100.0, "type": 2 },
                    { "icon": null, "name": null, "progress": 0.0, "type": 3 }
                  ]
                },
                {
                  "areaId": 73, "areaName": "无光之森", "areaPic": "https://img/area-73.png",
                  "areaProgress": 41.25,
                  "itemList": []
                }
              ]
            },
            {
              "country": { "countryId": 3, "countryName": "黎那汐塔", "homePageImage": "https://img/rin.png" },
              "countryProgress": "",
              "areaInfoList": []
            }
          ]
        }
        """;

    private static ExploreIndexData Sample()
        => JsonSerializer.Deserialize(SampleJson, ExploreJsonContext.Default.ExploreIndexData)!;

    [Fact]
    public void Deserialize_Maps_All_Countries_Areas_And_Items()
    {
        var data = Sample();
        Assert.True(data.Open);
        Assert.Single(data.DetectionInfoList ?? []);
        Assert.Equal(2, data.ExploreList?.Count);

        var first = data.ExploreList![0];
        Assert.Equal(1, first.Country?.CountryId);
        Assert.Equal("68.40", first.CountryProgress); // 原样保存字符串,解析交给 ExploreParser
        Assert.Equal(2, first.AreaInfoList?.Count);
        var firstArea = first.AreaInfoList![0];
        Assert.Equal(72.5, firstArea.AreaProgress);
        Assert.Equal(3, firstArea.ItemList?.Count);
        Assert.Equal(100.0, firstArea.ItemList![1].Progress);
        Assert.Equal(1, firstArea.ItemList[0].Type);
    }

    [Fact]
    public void ParsePercent_Handles_String_Wrapped_Values()
    {
        Assert.Equal(68.4, ExploreParser.ParsePercent("68.40"));
        Assert.Equal(100, ExploreParser.ParsePercent("100"));
        Assert.Equal(45.5, ExploreParser.ParsePercent("45.5%"));
        Assert.Equal(9.5, ExploreParser.ParsePercent(" 9.5 "));
        // 空/异常一律 null(界面据此显示 "--"),不能退化成 0
        Assert.Null(ExploreParser.ParsePercent(""));
        Assert.Null(ExploreParser.ParsePercent("   "));
        Assert.Null(ExploreParser.ParsePercent(null));
        Assert.Null(ExploreParser.ParsePercent("N/A"));
    }

    [Fact]
    public void FormatPercent_Keeps_One_Decimal_And_Drops_Trailing_Zero()
    {
        Assert.Equal("45.2%", ExploreParser.FormatPercent(45.2));
        Assert.Equal("100%", ExploreParser.FormatPercent(100.0));
        Assert.Equal("0%", ExploreParser.FormatPercent(0));
        Assert.Equal("72.5%", ExploreParser.FormatPercent(72.50));
    }

    [Fact]
    public void ClampPercent_Keeps_Bars_Inside_Range()
    {
        Assert.Equal(0, ExploreParser.ClampPercent(-5));
        Assert.Equal(100, ExploreParser.ClampPercent(120));
        Assert.Equal(63.5, ExploreParser.ClampPercent(63.5));
        Assert.Equal(0, ExploreParser.ClampPercent(double.NaN));
    }

    [Fact]
    public void ResolveImageUrl_Skips_Directory_Prefixes_And_Picks_First_Real_Url()
    {
        // 第一候选是目录前缀(以 / 结尾)→ 必须跳过
        Assert.Equal("https://img/a.png", ExploreParser.ResolveImageUrl("https://img/dir/", "https://img/a.png"));
        Assert.Null(ExploreParser.ResolveImageUrl("https://img/dir/", "", null));
        Assert.Null(ExploreParser.ResolveImageUrl());
    }

    [Fact]
    public void ResolveImageUrl_Prefixes_Relative_Collectible_Icons()
    {
        // 实测(真实账号数据):探索物图标下发的是相对路径,不补前缀在客户端就是无效地址
        Assert.Equal("https://web-static.kurobbs.com/admin/Config/33/project_icon/123.png",
            ExploreParser.ResolveImageUrl("Config/33/project_icon/123.png"));
        // 已经带 adminConfig 段的路径只需补主机
        Assert.Equal("https://web-static.kurobbs.com/adminConfig/74/home_page_icon/1.png",
            ExploreParser.ResolveImageUrl("adminConfig/74/home_page_icon/1.png"));
        Assert.Equal("https://web-static.kurobbs.com/admin/Config/33/x.png",
            ExploreParser.ResolveImageUrl("/Config/33/x.png"));
        // 协议相对地址与带 scheme 的伪地址不予拼装(拼出来必然 404,不如不显示图)
        Assert.Null(ExploreParser.ResolveImageUrl("//cdn.example.com/a.png"));
        Assert.Null(ExploreParser.ResolveImageUrl("data:image/png;base64,AAAA"));
        Assert.Null(ExploreParser.ResolveImageUrl("file:///C:/x.png"));
    }

    [Fact]
    public void BuildExploreCountry_Maps_Progress_Icon_And_Areas()
    {
        var country = TowerViewModel.BuildExploreCountry(Sample().ExploreList![0]);

        Assert.Equal(1, country.CountryId);
        Assert.Equal("朔原", country.Name);
        Assert.Equal("68.4%", country.ProgressText);
        Assert.Equal(68.4, country.ProgressPercent);
        // homePageIcon 是目录前缀 → 落到次选的 homePageImage
        Assert.Equal("https://img/hl.png", country.MainImageUrl);
        Assert.True(country.HasMainImage);
        Assert.Equal(2, country.Areas.Count);
        Assert.Equal(LanguageService.Format("Tower.Explore.AreaCount", 2), country.AreaCountText);
    }

    [Fact]
    public void BuildExploreCountry_Prefers_Wide_Home_Image_For_The_Big_Card()
    {
        var country = TowerViewModel.BuildExploreCountry(new ExploreCountryEntry
        {
            Country = new ExploreCountryInfo
            {
                CountryId = 900,
                CountryName = "黑海岸",
                HomePageImage = "https://img/wide-card.png",   // 1.63:1,大卡上最合适
                HomePageIcon = "https://img/square-icon.png",  // 正方形,放大后会把卡片撑得很高
                DetailPagePic = "https://img/wide-namecard.png", // 4.63:1,压到 190px 宽只剩 41px 高
            },
            CountryProgress = "12.50",
        });

        Assert.Equal("https://img/wide-card.png", country.MainImageUrl);
    }

    [Fact]
    public void BuildExploreCountry_Without_Progress_Shows_Dash_Instead_Of_Zero()
    {
        var country = TowerViewModel.BuildExploreCountry(Sample().ExploreList![1]);

        Assert.Equal("黎那汐塔", country.Name);
        Assert.Equal("--", country.ProgressText);
        Assert.Equal(0, country.ProgressPercent); // 环压成 0,不画假的进度
        Assert.Empty(country.Areas);
        // 没有区域时主图仍取得到
        Assert.Equal("https://img/rin.png", country.MainImageUrl);
    }

    [Fact]
    public void BuildExploreArea_Maps_Items_And_Falls_Back_For_Missing_Name()
    {
        var area = TowerViewModel.BuildExploreArea(Sample().ExploreList![0].AreaInfoList![0]);

        Assert.Equal(46, area.AreaId);
        Assert.Equal("云陵谷", area.Name);
        Assert.Equal("72.5%", area.ProgressText);
        Assert.Equal("https://img/area-46.png", area.PicUrl);
        Assert.True(area.HasItems);
        Assert.Equal(3, area.Items.Count);

        var qiZang = area.Items[0];
        Assert.Equal("奇藏", qiZang.Name);
        Assert.Equal("80%", qiZang.ProgressText);
        Assert.Equal("https://img/i-1.png", qiZang.IconUrl);
        Assert.True(qiZang.HasIcon);

        // 相对路径图标(真实数据形态)必须补上静态资源前缀,否则界面拿到的是无效地址
        Assert.Equal("https://web-static.kurobbs.com/admin/Config/33/project_icon/beacon.png",
            area.Items[1].IconUrl);

        // 名字/图标都缺失的那一项:不能变成空白标签,要给兜底文案
        var unknown = area.Items[2];
        Assert.Equal(LanguageService.Format("Tower.Explore.UnknownItem"), unknown.Name);
        Assert.False(unknown.HasIcon);
        Assert.Equal("0%", unknown.ProgressText);
    }

    [Fact]
    public void BuildExploreArea_Clamps_Progress_And_Handles_No_Items()
    {
        var area = TowerViewModel.BuildExploreArea(new ExploreArea
        {
            AreaId = 73,
            AreaName = "无光之森",
            AreaPic = "https://img/area-73.png",
            AreaProgress = 140.0,
            ItemList = [],
        });

        Assert.Equal("100%", area.ProgressText);
        Assert.Equal(100, area.ProgressPercent);
        Assert.False(area.HasItems);
    }

    [Fact]
    public void BuildExploreArea_Falls_Back_To_Area_Id_When_Name_Missing()
    {
        var area = TowerViewModel.BuildExploreArea(new ExploreArea { AreaId = 88, AreaName = "  ", AreaProgress = 10 });

        Assert.Equal(LanguageService.Format("Tower.Explore.UnknownArea", 88), area.Name);
        Assert.False(area.HasPic);
    }

    /// <summary>接口给的 exploreList 项(区域 id 用真实值:2/3 云陵谷·今州城属今州,46/44001 属梦州)。</summary>
    private static ExploreCountryEntry Entry(int countryId, string countryName, params int[] areaIds)
        => new()
        {
            Country = new ExploreCountryInfo { CountryId = countryId, CountryName = countryName },
            CountryProgress = "100.00",
            AreaInfoList = areaIds.Select(id => new ExploreArea
            {
                AreaId = id,
                AreaName = "区域" + id,
                AreaProgress = 100,
            }).ToList(),
        };

    [Fact]
    public void BuildExploreCountry_Splits_Huanglong_Into_Jinzhou_And_Mengzhou()
    {
        // 瑝珑:今州(2/3/…)与梦州(46 梦枢天罗 + 44xxx 玄方地界)是两级 —— 接口不返回这个层级,
        // 由 ExploreRegionCatalog 按 areaId 归类
        var country = TowerViewModel.BuildExploreCountry(
            Entry(1, "瑝珑", 2, 3, 46, 44001, 44002));

        Assert.Equal(2, country.SubRegions.Count);
        var jinzhou = country.SubRegions[0];
        var mengzhou = country.SubRegions[1];
        Assert.Equal(LanguageService.Format("Tower.Explore.Sub.Jinzhou"), jinzhou.Name);
        Assert.Equal(LanguageService.Format("Tower.Explore.Sub.Mengzhou"), mengzhou.Name);
        Assert.Equal(new[] { 2, 3 }, jinzhou.Areas.Select(a => a.AreaId));
        Assert.Equal(new[] { 46, 44001, 44002 }, mengzhou.Areas.Select(a => a.AreaId));
        // 子地区没有官方探索度 → 取各区域平均(这里全是 100)
        Assert.Equal("100%", jinzhou.ProgressText);
        // 一级项仍然持有全部区域(点"瑝珑"看全部)
        Assert.Equal(5, country.Areas.Count);
    }

    [Fact]
    public void BuildExploreCountry_Unknown_Area_Falls_Into_Other_Group()
    {
        // 新增区域没登记时不能丢:落到「其他区域」,补一行 id 即可归位
        var country = TowerViewModel.BuildExploreCountry(Entry(1, "瑝珑", 2, 99999));

        Assert.Equal(2, country.SubRegions.Count);
        var other = country.SubRegions[1];
        Assert.Equal(LanguageService.Format("Tower.Explore.Sub.Other"), other.Name);
        Assert.Equal(new[] { 99999 }, other.Areas.Select(a => a.AreaId));
    }

    [Fact]
    public void BuildExploreCountry_Without_Catalog_Stays_Flat()
    {
        // 黑海岸只有一个城邦(黑海岸群岛 + 泰缇斯之底):不做无意义的嵌套
        var country = TowerViewModel.BuildExploreCountry(Entry(900, "黑海岸", 11, 15));

        Assert.Empty(country.SubRegions);
    }

    [Fact]
    public void BuildExploreCountry_Splits_RoyIcefield_Into_Three_SubRegions()
    {
        // 罗伊冰原:拉海洛(3.0)/冰原地表(3.1)/黯原(3.3),三组区域名单与官方版本说明一致
        var country = TowerViewModel.BuildExploreCountry(Entry(4, "罗伊冰原", 36102, 41001, 42001));

        Assert.Equal(3, country.SubRegions.Count);
        Assert.Equal(LanguageService.Format("Tower.Explore.Sub.LahaiRoi"), country.SubRegions[0].Name);
        Assert.Equal(LanguageService.Format("Tower.Explore.Sub.IceSurface"), country.SubRegions[1].Name);
        Assert.Equal(LanguageService.Format("Tower.Explore.Sub.DarkPlain"), country.SubRegions[2].Name);
        Assert.Equal(new[] { 36102 }, country.SubRegions[0].Areas.Select(a => a.AreaId));
        Assert.Equal(new[] { 41001 }, country.SubRegions[1].Areas.Select(a => a.AreaId));
        Assert.Equal(new[] { 42001 }, country.SubRegions[2].Areas.Select(a => a.AreaId));
    }

    [Fact]
    public void BuildExploreCountry_Splits_Rinascita_Into_Ragunna_And_Septimont()
    {
        // 黎那汐塔:拉古那(拉古那城/埃弗拉德金库…13 个) 与 七丘(七丘本体 + 桑古伊斯狩原)
        var country = TowerViewModel.BuildExploreCountry(Entry(3, "黎那汐塔", 16, 34, 33, 36));

        Assert.Equal(2, country.SubRegions.Count);
        Assert.Equal(LanguageService.Format("Tower.Explore.Sub.Ragunna"), country.SubRegions[0].Name);
        Assert.Equal(LanguageService.Format("Tower.Explore.Sub.Septimont"), country.SubRegions[1].Name);
        Assert.Equal(new[] { 16, 34 }, country.SubRegions[0].Areas.Select(a => a.AreaId));
        Assert.Equal(new[] { 33, 36 }, country.SubRegions[1].Areas.Select(a => a.AreaId));
    }

    [Fact]
    public void BuildAreaRows_Pairs_Areas_Two_Per_Row()
    {
        var areas = new[] { 1, 2, 3, 4, 5 }
            .Select(id => TowerViewModel.BuildExploreArea(new ExploreArea { AreaId = id, AreaName = "区域" + id }))
            .ToList();

        var rows = TowerViewModel.BuildAreaRows(areas, columns: 2);

        Assert.Equal(3, rows.Count);
        Assert.Equal(new[] { 1, 2 }, rows[0].Cells.Select(c => c!.AreaId));
        Assert.Equal(new[] { 3, 4 }, rows[1].Cells.Select(c => c!.AreaId));
        // 奇数个区域时最后一行右侧补 null 占位(不能变成"整行一张卡")
        Assert.Equal(5, rows[2].Cells[0]!.AreaId);
        Assert.Null(rows[2].Cells[1]);

        // 宽窗口多分列:4 列时 5 个区域 → 2 行(首行 1..4,次行只剩第 5 个 + 3 个空位)
        var wide = TowerViewModel.BuildAreaRows(areas, columns: 4);
        Assert.Equal(2, wide.Count);
        Assert.Equal(4, wide[0].Cells.Count);
        Assert.Equal(4, wide[0].Columns);
        Assert.Equal(new[] { 1, 2, 3, 4 }, wide[0].Cells.Select(c => c!.AreaId));
        Assert.Equal(5, wide[1].Cells[0]!.AreaId);
        Assert.Equal(new int?[] { null, null, null }, wide[1].Cells.Skip(1).Select(c => c?.AreaId));
    }

    [Fact]
    public void ClampAreaColumns_Keeps_Column_Count_Sane()
    {
        // 视图按宽度算出来的列数会落到 1 或 10+,这里必须夹住:1 列主图会被拉成横幅,太多列又读不出数据
        Assert.Equal(2, TowerViewModel.ClampAreaColumns(0));
        Assert.Equal(2, TowerViewModel.ClampAreaColumns(1));
        Assert.Equal(3, TowerViewModel.ClampAreaColumns(3));
        Assert.Equal(6, TowerViewModel.ClampAreaColumns(9));
    }

    [Fact]
    public void BuildExploreNodes_Flattens_Countries_And_SubRegions()
    {
        var countries = new[]
        {
            TowerViewModel.BuildExploreCountry(Entry(1, "瑝珑", 2, 46)),
            TowerViewModel.BuildExploreCountry(Entry(900, "黑海岸", 11)),
        };

        var nodes = TowerViewModel.BuildExploreNodes(countries);

        // 瑝珑(全部) + 今州 + 梦州 + 黑海岸 = 4 项
        Assert.Equal(4, nodes.Count);
        Assert.False(nodes[0].IsSubRegion);
        Assert.Equal("瑝珑", nodes[0].Name);
        Assert.Equal(countries[0].AreaCountText, nodes[0].AreaCountText); // 一级项显示该地区"全部区域"数
        Assert.Equal(2, nodes[0].Areas.Count); // 一级项 = 全部区域

        Assert.True(nodes[1].IsSubRegion);
        Assert.Equal(LanguageService.Format("Tower.Explore.SubTitle", "瑝珑", LanguageService.Format("Tower.Explore.Sub.Jinzhou")),
            nodes[1].HeaderText);
        Assert.Null(nodes[1].ImageUrl); // 二级项不显示主图
        Assert.Equal(new[] { 2 }, nodes[1].Areas.Select(a => a.AreaId));

        Assert.False(nodes[3].IsSubRegion);
        Assert.Equal("黑海岸", nodes[3].Name);
    }

    [Fact]
    public void BuildExploreNodes_Marks_Collapsible_Parents()
    {
        var countries = new[]
        {
            TowerViewModel.BuildExploreCountry(Entry(1, "瑝珑", 2, 46)),
            TowerViewModel.BuildExploreCountry(Entry(900, "黑海岸", 11)),
        };

        var nodes = TowerViewModel.BuildExploreNodes(countries);

        Assert.True(nodes[0].HasChildren);        // 瑝珑有二级地区 → 可展开/收起
        Assert.False(nodes[1].HasChildren);       // 今州本身不再有下级
        Assert.Equal(1, nodes[1].ParentCountryId); // 二级项指回瑝珑
        Assert.False(nodes[3].HasChildren);       // 黑海岸没有二级地区 → 点它只切换选中
        Assert.True(nodes[0].IsExpanded);         // 默认展开
    }

    [Fact]
    public void VisibleNodesOf_Hides_SubRegions_When_Parent_Collapsed()
    {
        var countries = new[]
        {
            TowerViewModel.BuildExploreCountry(Entry(1, "瑝珑", 2, 46)),
            TowerViewModel.BuildExploreCountry(Entry(900, "黑海岸", 11)),
        };
        var nodes = TowerViewModel.BuildExploreNodes(countries);

        // 默认全展开:瑝珑 + 今州 + 梦州 + 黑海岸
        Assert.Equal(4, TowerViewModel.VisibleNodesOf(nodes).Count);

        // 收起瑝珑 → 只剩瑝珑本身与黑海岸
        nodes[0].IsExpanded = false;
        var visible = TowerViewModel.VisibleNodesOf(nodes);
        Assert.Equal(2, visible.Count);
        Assert.Equal(new[] { "瑝珑", "黑海岸" }, visible.Select(n => n.Name));

        // 再展开 → 二级项回来
        nodes[0].IsExpanded = true;
        Assert.Equal(4, TowerViewModel.VisibleNodesOf(nodes).Count);
    }
}
