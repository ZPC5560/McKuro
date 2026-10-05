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
                new GuideResonanceItem { ResonanceSequence = 1, IsAcquired = true, Texts = [Zh("一链")], PictureUrl = "http://img/chain1.png" },
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

        // 共鸣链:序号/名称/解锁/图标
        Assert.Equal(2, detail.Chains?.Count);
        Assert.Equal(1, detail.Chains?[0].ChainNum);
        Assert.Equal("一链", detail.Chains?[0].ChainName);
        Assert.True(detail.Chains?[0].IsUnlock);
        Assert.Equal("http://img/chain1.png", detail.Chains?[0].IconUrl);
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

    // ---------------- 官方推荐判定(角色详情页推荐区) ----------------

    [Fact]
    public void WeaponRecommendation_Maps_Status_Tiers()
    {
        // 实测 1504 灯灯:items 内 status=1 首选(时和岁稔)/2 备选(浩境粼光)/0 未收录(纹秋)
        Assert.True(GuideAchievementService.IsRecommendedWeapon(new GuideWeaponItem { Status = 1 }));
        Assert.True(GuideAchievementService.IsRecommendedWeapon(new GuideWeaponItem { Status = 2 }));
        Assert.False(GuideAchievementService.IsRecommendedWeapon(new GuideWeaponItem { Status = 0 }));

        Assert.Equal("推荐", GuideAchievementService.WeaponRecommendText(new GuideWeaponItem { Status = 1 }));
        Assert.Equal("备选", GuideAchievementService.WeaponRecommendText(new GuideWeaponItem { Status = 2 }));
        Assert.Equal("", GuideAchievementService.WeaponRecommendText(new GuideWeaponItem { Status = 0 }));
    }

    [Fact]
    public void MatchEquippedWeapon_Normalizes_Separators_And_Null_When_Unknown()
    {
        // 实测 1303 渊武:库街区返回「源能臂铠·测肆」,攻略同样含间隔号 → 必须命中
        var items = new List<GuideWeaponItem>
        {
            new() { Status = 1, Texts = [Zh("源能臂铠·测肆")] },
            new() { Status = 2, Texts = [Zh("诸方玄枢")] },
        };
        Assert.NotNull(GuideAchievementService.MatchEquippedWeapon("源能臂铠·测肆", items));
        Assert.NotNull(GuideAchievementService.MatchEquippedWeapon("源能臂铠测肆", items));   // 间隔号差异
        Assert.NotNull(GuideAchievementService.MatchEquippedWeapon(" 源能臂铠 · 测肆 ", items)); // 空白差异
        Assert.Equal(2, GuideAchievementService.MatchEquippedWeapon("诸方玄枢", items)!.Status);

        // ★ 竞态核心:武器名未知(库街区详情未到)必须返回 null,调用方据此保持中性,
        //   不得把它当成"确认不匹配"渲染成「有差距」。
        Assert.Null(GuideAchievementService.MatchEquippedWeapon(null, items));
        Assert.Null(GuideAchievementService.MatchEquippedWeapon("", items));
        Assert.Null(GuideAchievementService.MatchEquippedWeapon("   ", items));

        // 详情已到但确实不在列表里 → null,由调用方判定「有差距」
        Assert.Null(GuideAchievementService.MatchEquippedWeapon("纹秋", items));
        // 无推荐列表 → null
        Assert.Null(GuideAchievementService.MatchEquippedWeapon("源能臂铠·测肆", []));
        Assert.Null(GuideAchievementService.MatchEquippedWeapon("源能臂铠·测肆", null));
    }

    [Fact]
    public void PhantomRecommendation_Matches_Main_Spare_Current()
    {
        var echo = new GuideEcho
        {
            Main = new GuideEchoBuild { EchoProps = new GuideEchoProps { Texts = [Zh("无常凶鹭")] } },
            Spare = new GuideEchoBuild { EchoProps = new GuideEchoProps { Texts = [Zh("梦魇·云闪之鳞")] } },
        };
        // 主推荐/备选命中(名称归一化:去空白/间隔号)
        Assert.True(GuideAchievementService.IsRecommendedPhantom("无常凶鹭", echo));
        Assert.True(GuideAchievementService.IsRecommendedPhantom("梦魇·云闪之鳞", echo));
        Assert.True(GuideAchievementService.IsRecommendedPhantom("无常 凶鹭", echo)); // 空白差异
        // 未命中
        Assert.False(GuideAchievementService.IsRecommendedPhantom("鸣钟之龟", echo));
        Assert.False(GuideAchievementService.IsRecommendedPhantom("", echo));
        Assert.False(GuideAchievementService.IsRecommendedPhantom("无常凶鹭", (GuideEcho?)null));
    }

    [Fact]
    public void SkillLevel_Met_Compares_Recommend_And_Current()
    {
        static GuideSkillTarget Target(int rec, int cur) => new()
        {
            RecommendLevel = System.Text.Json.JsonSerializer.SerializeToElement(rec),
            CurrentLevel = System.Text.Json.JsonSerializer.SerializeToElement(cur),
        };

        Assert.True(GuideAchievementService.IsSkillLevelMet(Target(8, 8)));   // 已达标
        Assert.True(GuideAchievementService.IsSkillLevelMet(Target(8, 10)));  // 超过推荐
        Assert.False(GuideAchievementService.IsSkillLevelMet(Target(8, 1)));  // 未达标
        Assert.Null(GuideAchievementService.IsSkillLevelMet(Target(0, 5)));   // 攻略无推荐等级

        Assert.Contains("Lv.8", GuideAchievementService.SkillRecommendText(Target(8, 1)));
        Assert.Contains("无需升级", GuideAchievementService.SkillRecommendText(Target(0, 1)));
    }

    /// <summary>
    /// 回归(用户反馈「技能加点已经达标还显示需要提升」):
    /// 攻略接口的 currentLevel 是<b>服务端快照</b>(且被本地缓存 24h),玩家点完技能后长期滞后;
    /// 库街区实时等级才是权威值。传入 liveCurrentLevel 时必须覆盖快照。
    /// <para>
    /// 用例取真实数据(角色「心」cardRoleId=1311,2026-10-05 实测):
    /// 常态攻击 推荐8 / 攻略快照6 / 库街区实时10 —— 旧实现据此误报「建议提升至 8 级」,
    /// 而页面上的技能弧线徽章同时显示 10/8(自相矛盾)。
    /// </para>
    /// </summary>
    [Fact]
    public void SkillLevel_Met_Prefers_Live_Level_Over_Stale_Guide_Snapshot()
    {
        static GuideSkillTarget Target(int rec, int snapshotCur) => new()
        {
            RecommendLevel = System.Text.Json.JsonSerializer.SerializeToElement(rec),
            CurrentLevel = System.Text.Json.JsonSerializer.SerializeToElement(snapshotCur),
        };

        // 快照 6 < 推荐 8 ⇒ 旧行为误报未达标
        Assert.False(GuideAchievementService.IsSkillLevelMet(Target(8, 6)));
        // 实时 10 ≥ 推荐 8 ⇒ 已达标,不得再提示提升
        Assert.True(GuideAchievementService.IsSkillLevelMet(Target(8, 6), liveCurrentLevel: 10));

        // 共鸣回路:快照 9 < 推荐 10,实时 10 ⇒ 已达标
        Assert.False(GuideAchievementService.IsSkillLevelMet(Target(10, 9)));
        Assert.True(GuideAchievementService.IsSkillLevelMet(Target(10, 9), liveCurrentLevel: 10));

        // 共鸣解放:快照 6 < 推荐 10,实时 10 ⇒ 已达标
        Assert.True(GuideAchievementService.IsSkillLevelMet(Target(10, 6), liveCurrentLevel: 10));

        // 变奏技能:实时 6 < 推荐 10 ⇒ 真的未达标,提示必须保留(不能矫枉过正把提示清空)
        Assert.False(GuideAchievementService.IsSkillLevelMet(Target(10, 6), liveCurrentLevel: 6));

        // 实时值缺失(null)→ 回退快照,与旧行为一致
        Assert.False(GuideAchievementService.IsSkillLevelMet(Target(8, 6), liveCurrentLevel: null));

        // 推荐等级缺失时仍返回 null(与实时值无关)
        Assert.Null(GuideAchievementService.IsSkillLevelMet(Target(0, 6), liveCurrentLevel: 10));
    }

    [Fact]
    public void PlainRecommendText_Strips_Html()
    {
        Assert.Equal("共鸣链2提供无视20%防御", GuideAchievementService.PlainRecommendText("<p>共鸣链2提供无视20%防御</p>"));
        Assert.Equal("A > B", GuideAchievementService.PlainRecommendText("<p>A &gt; B</p>"));
        Assert.Equal("", GuideAchievementService.PlainRecommendText(null));
        Assert.Equal("", GuideAchievementService.PlainRecommendText(""));
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
