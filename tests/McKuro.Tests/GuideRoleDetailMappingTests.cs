using System.Net;
using System.Text;
using McKuro.Core.Models.Guide;
using McKuro.Core.Models.Roles;
using McKuro.Core.Services.CloudGame;
using McKuro.Core.Services.Guide;
using McKuro.Core.Services.Settings;

namespace McKuro.Tests;

/// <summary>
/// mcguide → 库街区 RoleDetail 映射测试:
/// MapRoleDetail 纯映射 + GetRoleDetailFromGuideAsync 端到端(本地 HttpListener 模拟 guide-server)。
/// </summary>
public class GuideRoleDetailMappingTests
{
    private sealed class FakeSettings : ISettingsService
    {
        public AppSettings Current { get; } = new();
        public void Save() { }
        public Task SaveAsync(CancellationToken ct = default) => Task.CompletedTask;
        public void Reload() { }
    }

    private static GuideTextItem Zh(string name) => new() { Language = "zh-Hans", Name = name };

    private static GuideIntroductionInfo BuildFullInfo() => new()
    {
        Role = new GuideRoleInfo
        {
            RoleGbId = "1209",
            Star = 5,
            Texts = [Zh("莫宁")],
        },
        Grade = "SS",
        Weapon = new GuideWeapon
        {
            Current = new GuideWeaponItem
            {
                Star = 5,
                PictureUrl = "http://img/weapon.png",
                Texts = [Zh("千古洑流")],
            },
        },
        RoleSkill = new GuideRoleSkill
        {
            FixedSkills =
            [
                new GuideFixedSkill
                {
                    PictureUrl = "http://img/skill1.png",
                    SkillType = new GuideSkillType { Texts = [Zh("普攻")] },
                    Texts = [new GuideTextItem { Language = "zh-Hans", Name = "普攻", Description = "普通攻击" }],
                },
                new GuideFixedSkill
                {
                    PictureUrl = "http://img/skill2.png",
                    Texts = [new GuideTextItem { Language = "zh-Hans", Name = "共鸣技能", Description = "技能描述" }],
                },
            ],
        },
        RoleAttribute = new GuideRoleAttribute
        {
            Items =
            [
                new GuideAttributeItem
                {
                    PictureUrl = "http://img/attr1.png",
                    Texts = [Zh("暴击")],
                    RecommendAmount = "60.0%",
                    CurrentAmount = "67.5%",
                    IsFinished = true,
                },
                new GuideAttributeItem
                {
                    Texts = [Zh("暴击伤害")],
                    RecommendAmount = "270.0%",
                    CurrentAmount = "260.0%",
                    IsFinished = false,
                },
            ],
        },
        RoleResonance = new GuideRoleResonance
        {
            Items =
            [
                new GuideResonanceItem { ResonanceSequence = 1, IsAcquired = true, Texts = [Zh("一链")] },
                new GuideResonanceItem { ResonanceSequence = 2, IsAcquired = false, Texts = [Zh("二链")] },
            ],
        },
        Echo = new GuideEcho
        {
            Current = new GuideEchoBuild
            {
                EchoProps = new GuideEchoProps
                {
                    Star = 5,
                    Cost = 4,
                    PictureUrl = "http://img/echo.png",
                    Texts = [Zh("啸谷幼猿")],
                },
                EchoSetEffects = [new GuideEchoSetEffect { Texts = [Zh("凝夜白霜")] }],
            },
        },
    };

    [Fact]
    public void MapRoleDetail_Maps_All_Sections()
    {
        var detail = GuideAchievementService.MapRoleDetail(BuildFullInfo(), 1209);

        // 角色基础
        Assert.Equal(1209, detail.Role?.RoleId);
        Assert.Equal("莫宁", detail.RoleName);
        Assert.Equal(5, detail.StarLevel);

        // 武器:名称/星级/图标,等级/突破/精炼为 0
        Assert.NotNull(detail.WeaponData);
        Assert.Equal("千古洑流", detail.WeaponData!.DisplayName);
        Assert.Equal(5, detail.WeaponData.StarLevel);
        Assert.Equal("http://img/weapon.png", detail.WeaponData.Weapon?.WeaponIcon);
        Assert.Equal(0, detail.WeaponData.Level);
        Assert.Equal(0, detail.WeaponData.Breach);
        Assert.Equal(0, detail.WeaponData.Rank);

        // 技能:名称/图标,等级为 0
        Assert.Equal(2, detail.Skills?.Count);
        Assert.Equal("普攻", detail.Skills?[0].SkillName);
        Assert.Equal("http://img/skill1.png", detail.Skills?[0].Skill?.IconUrl);
        Assert.Equal("普攻", detail.Skills?[0].Skill?.Type);
        Assert.Equal(0, detail.Skills?[0].SkillLevel);
        Assert.Equal("共鸣技能", detail.Skills?[1].SkillName);

        // 属性:当前/推荐 拼接
        Assert.Equal(2, detail.Attributes?.Count);
        Assert.Equal("暴击", detail.Attributes?[0].AttributeName);
        Assert.Equal("67.5%/60.0%", detail.Attributes?[0].AttributeValue);
        Assert.Equal("已达标", detail.Attributes?[0].AttributeType);
        Assert.Equal("http://img/attr1.png", detail.Attributes?[0].IconUrl);
        Assert.Equal("未达标", detail.Attributes?[1].AttributeType);

        // 共鸣链:序号/名称/解锁
        Assert.Equal(2, detail.Chains?.Count);
        Assert.Equal(1, detail.Chains?[0].ChainNum);
        Assert.Equal("一链", detail.Chains?[0].ChainName);
        Assert.True(detail.Chains?[0].IsUnlock);
        Assert.False(detail.Chains?[1].IsUnlock);

        // 声骸:名称/图标/星级/套装
        Assert.NotNull(detail.PhantomData);
        var echo = Assert.Single(detail.PhantomData!.Phantoms ?? []);
        Assert.Equal("啸谷幼猿", echo.PhantomName);
        Assert.Equal("http://img/echo.png", echo.IconUrl);
        Assert.Equal(5, echo.Quality);
        Assert.Equal(4, echo.Cost);
        Assert.Equal("凝夜白霜", echo.FetterName);
    }

    [Fact]
    public void MapRoleDetail_Handles_Missing_Sections()
    {
        var info = new GuideIntroductionInfo
        {
            Role = new GuideRoleInfo { RoleGbId = "1209", Star = 4 },
        };
        var detail = GuideAchievementService.MapRoleDetail(info, 1209);

        Assert.Equal(1209, detail.Role?.RoleId);
        Assert.Equal("", detail.RoleName);
        Assert.Equal(4, detail.StarLevel);
        Assert.Null(detail.WeaponData);
        Assert.Empty(detail.Skills ?? []);
        Assert.Empty(detail.Attributes ?? []);
        Assert.Empty(detail.Chains ?? []);
        Assert.Null(detail.PhantomData);
    }

    [Fact]
    public void MapRoleDetail_Weapon_Falls_Back_To_Items()
    {
        var info = new GuideIntroductionInfo
        {
            Role = new GuideRoleInfo { RoleGbId = "1209", Star = 5 },
            Weapon = new GuideWeapon
            {
                Items = [new GuideWeaponItem { Star = 4, PictureUrl = "http://img/w2.png", Texts = [Zh("拂晓")] }],
            },
        };
        var detail = GuideAchievementService.MapRoleDetail(info, 1209);
        Assert.NotNull(detail.WeaponData);
        Assert.Equal("拂晓", detail.WeaponData!.DisplayName);
        Assert.Equal(4, detail.WeaponData.StarLevel);
        Assert.Equal("http://img/w2.png", detail.WeaponData.Weapon?.WeaponIcon);
    }

    [Fact]
    public async Task GetRoleDetailFromGuideAsync_Returns_Mapped_Detail()
    {
        var (baseUrl, listener) = StartGuideServer(new Dictionary<string, string>
        {
            ["/introduction/list"] =
                """
                {"code":200,"message":"ok","data":[
                  {"id":10162,"role":{"roleGbId":"1209","star":5,"texts":[{"language":"zh-Hans","name":"莫宁"}]},"likeCount":10},
                  {"id":10161,"role":{"roleGbId":"1209","star":5,"texts":[{"language":"zh-Hans","name":"莫宁"}]},"likeCount":99}
                ]}
                """,
            ["/introduction/info"] =
                """
                {"code":200,"message":"ok","data":{
                  "id":10161,
                  "grade":"SS",
                  "role":{"roleGbId":"1209","star":5,"texts":[{"language":"zh-Hans","name":"莫宁"}]},
                  "weapon":{"current":{"gbId":"21020086","star":5,"pictureUrl":"http://img/w.png","texts":[{"language":"zh-Hans","name":"千古洑流"}]}},
                  "roleSkill":{"fixedSkills":[
                    {"gbId":"1","pictureUrl":"http://img/s1.png","skillType":{"texts":[{"language":"zh-Hans","name":"普攻"}]},"texts":[{"language":"zh-Hans","name":"普攻"}]}
                  ]},
                  "roleAttribute":{"items":[
                    {"gbId":"8-2","texts":[{"language":"zh-Hans","name":"暴击"}],"recommendAmount":"60.0%","currentAmount":"67.5%","isFinished":true}
                  ]},
                  "roleResonance":{"items":[
                    {"resonanceSequence":1,"texts":[{"language":"zh-Hans","name":"一链"}],"isAcquired":true}
                  ]},
                  "echo":{"current":{"echoProps":{"star":5,"cost":4,"pictureUrl":"http://img/e.png","texts":[{"language":"zh-Hans","name":"啸谷幼猿"}]},"echoSetEffects":[{"texts":[{"language":"zh-Hans","name":"凝夜白霜"}]}]}}
                }}
                """,
        });
        try
        {
            var settings = new FakeSettings();
            settings.Current.GuideToken = "test-token";
            var service = CreateService(baseUrl, settings);

            var detail = await service.GetRoleDetailFromGuideAsync("莫宁", 1209);

            Assert.NotNull(detail);
            Assert.Equal("莫宁", detail!.RoleName);
            Assert.Equal(5, detail.StarLevel);
            Assert.Equal("千古洑流", detail.WeaponData?.DisplayName);
            var skill = Assert.Single(detail.Skills ?? []);
            Assert.Equal("普攻", skill.SkillName);
            var attr = Assert.Single(detail.Attributes ?? []);
            Assert.Equal("67.5%/60.0%", attr.AttributeValue);
            var chain = Assert.Single(detail.Chains ?? []);
            Assert.True(chain.IsUnlock);
            var echo = Assert.Single(detail.PhantomData?.Phantoms ?? []);
            Assert.Equal("啸谷幼猿", echo.PhantomName);
            Assert.Equal("凝夜白霜", echo.FetterName);
        }
        finally
        {
            listener.Stop();
        }
    }

    [Fact]
    public async Task GetRoleDetailFromGuideAsync_Returns_Null_Without_Token()
    {
        var (baseUrl, listener) = StartGuideServer(new Dictionary<string, string>());
        try
        {
            var settings = new FakeSettings(); // GuideToken 为空
            var service = CreateService(baseUrl, settings);
            var detail = await service.GetRoleDetailFromGuideAsync("莫宁", 1209);
            Assert.Null(detail);
        }
        finally
        {
            listener.Stop();
        }
    }

    [Fact]
    public async Task GetRoleDetailFromGuideAsync_Returns_Null_For_Invalid_CardRoleId()
    {
        var (baseUrl, listener) = StartGuideServer(new Dictionary<string, string>());
        try
        {
            var settings = new FakeSettings();
            settings.Current.GuideToken = "test-token";
            var service = CreateService(baseUrl, settings);
            var detail = await service.GetRoleDetailFromGuideAsync("漂泊者", 0);
            Assert.Null(detail);
        }
        finally
        {
            listener.Stop();
        }
    }

    // ---------------- 基础设施(与 GuideAchievementServiceTests 一致) ----------------

    private static (string BaseUrl, HttpListener Listener) StartGuideServer(Dictionary<string, string> responses)
    {
        var listener = new HttpListener();
        var prefix = $"http://127.0.0.1:{GetFreePort()}/";
        listener.Prefixes.Add(prefix);
        listener.Start();
        _ = Task.Run(async () =>
        {
            while (listener.IsListening)
            {
                try
                {
                    var ctx = await listener.GetContextAsync();
                    var path = ctx.Request.Url!.AbsolutePath;
                    if (responses.TryGetValue(path, out var body))
                    {
                        var bytes = Encoding.UTF8.GetBytes(body);
                        ctx.Response.StatusCode = 200;
                        ctx.Response.ContentLength64 = bytes.Length;
                        await ctx.Response.OutputStream.WriteAsync(bytes);
                    }
                    else
                    {
                        ctx.Response.StatusCode = 404;
                    }
                    ctx.Response.Close();
                }
                catch
                {
                    break;
                }
            }
        });
        return (prefix, listener);
    }

    private static int GetFreePort()
    {
        var l = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        l.Start();
        var port = ((System.Net.IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();
        return port;
    }

    private static GuideAchievementService CreateService(string guideBaseUrl, FakeSettings settings)
    {
        var cloud = new CloudGameService(new HttpClient(), "test-device");
        var api = new GuideApiClient(new HttpClient(), guideBaseUrl);
        return new GuideAchievementService(cloud, api, settings);
    }
}
