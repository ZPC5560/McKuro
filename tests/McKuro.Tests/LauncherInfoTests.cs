using System.Text.Json;
using McKuro.Core.Models.Gacha;
using McKuro.Core.Models.Game;
using McKuro.Core.Services.Game;
using McKuro.Core.Services.Launcher;
using Xunit;

namespace McKuro.Tests;

/// <summary>加入 IconCatalog 串行集合:本类断言 IconCatalog 的静态兜底行为,不能与注入远程目录的测试并行。</summary>
[Collection(IconCatalogCollection.Name)]
public class LauncherInfoTests
{
    [Fact]
    public void Deserialize_StarterJson_Succeeds()
    {
        // 与官方 launcher information 接口返回结构一致
        const string json = """
        {
          "guidance": {
            "desc": "暂无内容",
            "activity": {
              "title": "活动", "sort": 1, "functionSwitch": 1,
              "contents": [
                { "content": "活动一", "jumpUrl": "https://x", "time": "07-10" }
              ]
            },
            "notice": {
              "title": "公告", "sort": 2, "functionSwitch": 1,
              "contents": [
                { "content": "公告一", "jumpUrl": "https://y", "time": "07-29" }
              ]
            }
          },
          "slideshow": [
            { "url": "https://cdn/slide1.jpg", "jumpUrl": "https://bili", "md5": "abc", "carouselNotes": "备注" }
          ]
        }
        """;

        var info = JsonSerializer.Deserialize(json, LauncherInfoJsonContext.Default.LauncherInfo);

        Assert.NotNull(info);
        Assert.NotNull(info!.Slideshow);
        Assert.Single(info.Slideshow!);
        Assert.Equal("https://cdn/slide1.jpg", info.Slideshow![0].Url);
        Assert.Equal("备注", info.Slideshow[0].CarouselNotes);
        Assert.NotNull(info.Guidance);
        Assert.Equal("活动一", info.Guidance!.Activity!.Contents![0].Content);
        Assert.Equal("公告一", info.Guidance.Notice!.Contents![0].Content);
    }

    [Fact]
    public void IconCatalog_RoleAndWeapon_ReturnUrls()
    {
        // 角色:忌炎 (1404)
        var roleUrl = IconCatalog.GetRoleIconUrl(1404);
        Assert.Equal("https://mc.appfeng.com/ui/avatar/T_IconRoleHead256_11_UI.png", roleUrl);

        // 武器:五星武器 (21050096)
        var weaponUrl = IconCatalog.GetWeaponIconUrl(21050096);
        Assert.Equal("https://mc.appfeng.com/ui/weapon/T_IconWeapon21050096_UI.png", weaponUrl);

        // 未知 ID → 空串
        Assert.Equal("", IconCatalog.GetRoleIconUrl(999999));
    }

    /// <summary>
    /// 新五星角色/武器必须在目录中:缺失时 <see cref="IconCatalog.GetRoleIconUrl"/> 返回空串,
    /// 抽卡分析页五星列表/表格的头像位会留空(表现为"抽到新角色没有头像")。
    /// 数据源:mc.appfeng.com/json/avatar.json 与 weapon.json。
    /// </summary>
    [Theory]
    [InlineData(1311, "T_IconRoleHead256_75_UI")] // 心(梦州版本新增)
    [InlineData(1312, "T_IconRoleHead256_76_UI")] // 锁暝
    [InlineData(1212, "T_IconRoleHead256_74_UI")] // 景燃
    [InlineData(1413, "T_IconRoleHead256_73_UI")] // 清宵
    public void IconCatalog_NewFiveStarRoles_AreMapped(int resourceId, string expectedIcon)
    {
        var url = IconCatalog.GetRoleIconUrl(resourceId);
        Assert.Equal($"https://mc.appfeng.com/ui/avatar/{expectedIcon}.png", url);
    }

    [Theory]
    [InlineData(21010076, "T_IconWeapon21010076_UI")] // 千般渡
    [InlineData(21020106, "T_IconWeapon21020106_UI")] // 云琅
    [InlineData(21020107, "T_IconWeapon21020107_UI")] // 沉冥
    [InlineData(21050116, "T_IconWeapon21050116_UI")] // 玉阙玄华
    public void IconCatalog_NewFiveStarWeapons_AreMapped(int resourceId, string expectedIcon)
    {
        var url = IconCatalog.GetWeaponIconUrl(resourceId);
        Assert.Equal($"https://mc.appfeng.com/ui/weapon/{expectedIcon}.png", url);
    }

    [Fact]
    public void IconCatalog_GetIconUrl_ByRecordType()
    {
        var role = new GachaRecord { ResourceId = 1404, ResourceType = "角色", QualityLevel = 5 };
        Assert.StartsWith("https://mc.appfeng.com/ui/avatar/", IconCatalog.GetIconUrl(role));

        var weapon = new GachaRecord { ResourceId = 21050096, ResourceType = "武器", QualityLevel = 5 };
        Assert.StartsWith("https://mc.appfeng.com/ui/weapon/", IconCatalog.GetIconUrl(weapon));
    }

    [Fact]
    public void Deserialize_BackgroundData_Succeeds()
    {
        const string json = """
        {
          "functionSwitch": 1,
          "backgroundFile": "https://cdn/video.mp4",
          "backgroundFileType": 2,
          "firstFrameImage": "https://cdn/frame.webp",
          "slogan": "https://cdn/logo.png"
        }
        """;

        var bg = JsonSerializer.Deserialize(json, LauncherInfoJsonContext.Default.LauncherBackgroundData);

        Assert.NotNull(bg);
        Assert.Equal("https://cdn/video.mp4", bg!.BackgroundFile);
        Assert.Equal(2, bg.BackgroundFileType);
        Assert.Equal("https://cdn/frame.webp", bg.FirstFrameImage);
        Assert.Equal("https://cdn/logo.png", bg.Slogan);
    }

    [Fact]
    public void Deserialize_LauncherIndex_GetsBackgroundCode()
    {
        const string json = """
        { "functionCode": { "background": "PTj45kPbFHV7O3FHrxK8CaRjsTlV6DHX" } }
        """;

        var index = JsonSerializer.Deserialize(json, LauncherInfoJsonContext.Default.LauncherIndex);

        Assert.NotNull(index);
        Assert.Equal("PTj45kPbFHV7O3FHrxK8CaRjsTlV6DHX", index!.FunctionCode!.Background);
        Assert.Equal("PTj45kPbFHV7O3FHrxK8CaRjsTlV6DHX", index.ResolveBackgroundCode());
    }

    /// <summary>
    /// 启动器 3.0 改版后 index.json 把 functionCode 收进了 config 层:
    /// 只认顶层 functionCode 会取不到背景编码,启动页背景视频直接不播。
    /// </summary>
    [Fact]
    public void Deserialize_LauncherIndex_NewConfigLayer_GetsBackgroundCode()
    {
        const string json = """
        {
          "cdnList": [],
          "config": {
            "commandSwitch": 1,
            "functionCode": { "background": "e4ea009e702a4961be86c815d87dbe68" }
          }
        }
        """;

        var index = JsonSerializer.Deserialize(json, LauncherInfoJsonContext.Default.LauncherIndex);

        Assert.NotNull(index);
        Assert.Equal("e4ea009e702a4961be86c815d87dbe68", index!.ResolveBackgroundCode());
    }

    /// <summary>新版 config 层优先;两层同时存在时不取旧值。</summary>
    [Fact]
    public void LauncherIndex_ResolveBackgroundCode_PrefersNewLayer()
    {
        const string json = """
        {
          "functionCode": { "background": "OLD" },
          "config": { "functionCode": { "background": "NEW" } }
        }
        """;

        var index = JsonSerializer.Deserialize(json, LauncherInfoJsonContext.Default.LauncherIndex);

        Assert.Equal("NEW", index!.ResolveBackgroundCode());
    }

    /// <summary>
    /// 官服必须走新接口 /launcher/app/.../{serverCode}/information/... :
    /// 旧接口已停更(实测轮播图只剩 2 张,新接口 4 张),路径拼错就退回旧数据。
    /// </summary>
    [Fact]
    public void BuildInformationUrl_Official_UsesNewAppEndpoint()
    {
        Assert.True(LauncherInfoService.HasNewApi(GameServerType.Official));

        var url = LauncherInfoService.BuildInformationUrl(
            "https://cdn", GameServerType.Official, "zh-Hans", 1700000000);

        Assert.Equal(
            "https://cdn/launcher/app/10003_oLNgHF1CESo51DGHN2odtp40e3oI1HfZ/G152/official/information/zh-Hans.json?_t=1700000000",
            url);
    }

    /// <summary>旧接口地址仍要能拼出来(新接口全挂时的兜底)。</summary>
    [Fact]
    public void BuildLegacyInformationUrl_Official_KeepsOldShape()
    {
        var url = LauncherInfoService.BuildLegacyInformationUrl(
            "https://cdn", GameServerType.Official, "zh-Hans", 1700000000);

        Assert.Equal(
            "https://cdn/launcher/10003_Y8xXrXk65DqFHEDgApn3cpK5lfczpFx5/G152/information/zh-Hans.json?_t=1700000000",
            url);
    }

    /// <summary>
    /// 三个渠道各自有<b>不同</b>的新 appKey 与 serverCode(都在各自启动器包 KRApp.conf 里):
    /// 国际服包的 serverCode 也是 official —— 按渠道名硬猜(global/bilibili)会 404。
    /// </summary>
    [Theory]
    [InlineData(GameServerType.Official, "10003_oLNgHF1CESo51DGHN2odtp40e3oI1HfZ", "G152", "official", "zh-Hans")]
    [InlineData(GameServerType.Bilibili, "10004_IY6xmlAusbImBjWrpPj1OwNuirXEO44x", "G152", "bilibili", "zh-Hans")]
    [InlineData(GameServerType.Global, "50004_P7xcUZnEr1AXIGON25E6KjpOgTlVrg6e", "G153", "official", "zh-Hant")]
    public void BuildInformationUrl_AllChannels_UseNewAppEndpoint(
        GameServerType serverType, string appSegment, string gameId, string serverCode, string language)
    {
        Assert.True(LauncherInfoService.HasNewApi(serverType));

        var url = LauncherInfoService.BuildInformationUrl("https://cdn", serverType, language, 1);

        Assert.Equal(
            $"https://cdn/launcher/app/{appSegment}/{gameId}/{serverCode}/information/{language}.json?_t=1",
            url);
    }

    /// <summary>旧接口地址(各渠道各自的旧 appKey)仍要能拼出来:新接口全挂时是唯一兜底。</summary>
    [Theory]
    [InlineData(GameServerType.Bilibili, "10004_j5GWFuUFlb8N31Wi2uS3ZAVHcb7ZGN7y", "G152", "zh-Hans")]
    [InlineData(GameServerType.Global, "50004_obOHXFrFanqsaIEOmuKroCcbZkQRBC7c", "G153", "zh-Hant")]
    public void BuildLegacyInformationUrl_AllChannels_KeepOldShape(
        GameServerType serverType, string legacySegment, string gameId, string language)
    {
        var url = LauncherInfoService.BuildLegacyInformationUrl("https://cdn", serverType, language, 1);

        Assert.Equal($"https://cdn/launcher/{legacySegment}/{gameId}/information/{language}.json?_t=1", url);
    }

    /// <summary>新老接口的背景链路路径不同(新接口没有 launcher 层、多一个 serverCode)。</summary>
    [Fact]
    public void BuildBackgroundUrls_Official_DifferBetweenApiGenerations()
    {
        var newIndex = LauncherInfoService.BuildGameIndexUrl("https://cdn", GameServerType.Official, useNewApi: true);
        var oldIndex = LauncherInfoService.BuildGameIndexUrl("https://cdn", GameServerType.Official, useNewApi: false);
        var newBg = LauncherInfoService.BuildBackgroundUrl(
            "https://cdn", GameServerType.Official, "CODE", "zh-Hans", useNewApi: true);
        var oldBg = LauncherInfoService.BuildBackgroundUrl(
            "https://cdn", GameServerType.Official, "CODE", "zh-Hans", useNewApi: false);

        Assert.Equal(
            "https://cdn/launcher/game/10003_oLNgHF1CESo51DGHN2odtp40e3oI1HfZ/G152/official/index.json", newIndex);
        Assert.Equal(
            "https://cdn/launcher/launcher/10003_Y8xXrXk65DqFHEDgApn3cpK5lfczpFx5/G152/index.json", oldIndex);
        Assert.Equal(
            "https://cdn/launcher/game/10003_oLNgHF1CESo51DGHN2odtp40e3oI1HfZ/G152/official/background/CODE/zh-Hans.json",
            newBg);
        Assert.Equal(
            "https://cdn/launcher/10003_Y8xXrXk65DqFHEDgApn3cpK5lfczpFx5/G152/background/CODE/zh-Hans.json", oldBg);
    }
}
