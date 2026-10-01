using System.Text.Json;
using McKuro.Core.Models.Gacha;
using McKuro.Core.Models.Game;
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
    }
}
