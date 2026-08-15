using System.Text.Json;
using donet.Core.Models.Gacha;
using donet.Core.Models.Game;
using donet.Core.Services.Launcher;
using Xunit;

namespace donet.Tests;

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
