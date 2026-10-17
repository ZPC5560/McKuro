using Sparkle.Live2DView;

namespace McKuro.Tests;

/// <summary>
/// <see cref="Live2DParameterMap"/> 的语义角色解析。
///
/// 背景:官方 SDK 把"头部转动/身体摇摆/呼吸/视线"硬编码成 <c>ParamAngleX</c> 等官方参数名,
/// 第三方「分层 PSD → Cubism」导出器生成的却是 <c>ParamPL_param_head_yaw_…</c>。
/// 两边对不上时,写入会静默落进 SDK 的"不存在参数"旁路表,模型因此看起来完全静止。
/// </summary>
public class Live2DParameterMapTests
{
    /// <summary>真实 PuppetLoom 模型的 cdi3 片段(取自 心-红黑/model.cdi3.json)。</summary>
    private const string PuppetLoomCdi3 = """
    {
      "Version": 3,
      "Parameters": [
        { "Id": "ParamPL_param_head_yaw_80184865e1e5", "Name": "Head Yaw", "GroupId": "GroupPuppetLoomHead" },
        { "Id": "ParamPL_param_head_pitch_8dedbfee3591", "Name": "Head Pitch", "GroupId": "GroupPuppetLoomHead" },
        { "Id": "ParamPL_param_head_roll_a14ac09ff14a", "Name": "Head Roll", "GroupId": "GroupPuppetLoomHead" },
        { "Id": "ParamPL_param_body_sway_236bb03fd820", "Name": "Body Sway", "GroupId": "GroupPuppetLoomBody" },
        { "Id": "ParamPL_param_body_pitch_3a4139a82bc5", "Name": "Body Pitch", "GroupId": "GroupPuppetLoomBody" },
        { "Id": "ParamPL_param_body_roll_4df13af85c9a", "Name": "Body Roll", "GroupId": "GroupPuppetLoomBody" },
        { "Id": "ParamPL_param_gaze_x_55eed3e96610", "Name": "Gaze X", "GroupId": "GroupPuppetLoomEyes" },
        { "Id": "ParamPL_param_gaze_y_b8cd88c8b40e", "Name": "Gaze Y", "GroupId": "GroupPuppetLoomEyes" },
        { "Id": "ParamPL_param_breath_fe85afa832bc", "Name": "Breath", "GroupId": "GroupPuppetLoomBody" },
        { "Id": "ParamPL_limb_idle_arm_L_ece39ae0ef68", "Name": "手臂轻摆-L", "GroupId": "GroupPuppetLoomParameter" }
      ]
    }
    """;

    [Fact]
    public void 解析出全部九个语义角色()
    {
        Live2DParameterMap map = Live2DParameterMap.FromDisplayInfoJson(PuppetLoomCdi3);

        Assert.True(map.Count >= 9, $"实际只解析出 {map.Count} 个角色");
        Assert.Equal("ParamPL_param_head_yaw_80184865e1e5", map.Resolve(Live2DSemanticParameter.HeadYaw));
        Assert.Equal("ParamPL_param_body_sway_236bb03fd820", map.Resolve(Live2DSemanticParameter.BodySway));
        Assert.Equal("ParamPL_param_breath_fe85afa832bc", map.Resolve(Live2DSemanticParameter.Breath));
        Assert.Equal("ParamPL_param_gaze_x_55eed3e96610", map.Resolve(Live2DSemanticParameter.GazeX));
        Assert.Equal("ParamPL_param_gaze_y_b8cd88c8b40e", map.Resolve(Live2DSemanticParameter.GazeY));
        Assert.Equal("ParamPL_param_head_roll_a14ac09ff14a", map.Resolve(Live2DSemanticParameter.HeadRoll));
        Assert.Equal("ParamPL_param_body_roll_4df13af85c9a", map.Resolve(Live2DSemanticParameter.BodyRoll));
    }

    /// <summary>
    /// 肢体参数(手臂轻摆)必须<b>不</b>被认成任何语义角色,否则程序化待机会和
    /// motion3.json 抢同一批参数,把自带动作顶掉。
    /// </summary>
    [Fact]
    public void 肢体参数不会被误认成语义角色()
    {
        Live2DParameterMap map = Live2DParameterMap.FromDisplayInfoJson(PuppetLoomCdi3);

        Assert.DoesNotContain("ParamPL_limb_idle_arm_L_ece39ae0ef68", map.Resolved.Values);
    }

    /// <summary>官方命名必须能直接命中 —— 保证 Hiyori 等官方模型不受影响。</summary>
    [Theory]
    [InlineData("ParamAngleX", Live2DSemanticParameter.HeadYaw)]
    [InlineData("ParamAngleY", Live2DSemanticParameter.HeadPitch)]
    [InlineData("ParamAngleZ", Live2DSemanticParameter.HeadRoll)]
    [InlineData("ParamBodyAngleX", Live2DSemanticParameter.BodySway)]
    [InlineData("ParamEyeBallX", Live2DSemanticParameter.GazeX)]
    [InlineData("ParamEyeBallY", Live2DSemanticParameter.GazeY)]
    [InlineData("ParamBreath", Live2DSemanticParameter.Breath)]
    [InlineData("ParamEyeLOpen", Live2DSemanticParameter.BlinkLeft)]
    [InlineData("ParamEyeROpen", Live2DSemanticParameter.BlinkRight)]
    [InlineData("ParamMouthOpenY", Live2DSemanticParameter.MouthOpen)]
    public void 官方参数Id直接命中对应角色(string id, Live2DSemanticParameter expected)
    {
        Assert.Equal(expected, Live2DParameterMap.Match(null, id));
    }

    /// <summary>
    /// <c>ParamBodyAngleX</c> 含 "anglex" 子串,若先判头部规则会被错认成 HeadYaw ——
    /// 这是最容易写错的一条顺序依赖。
    /// </summary>
    [Fact]
    public void 身体规则优先于头部规则()
    {
        Assert.Equal(Live2DSemanticParameter.BodySway, Live2DParameterMap.Match(null, "ParamBodyAngleX"));
        Assert.Equal(Live2DSemanticParameter.BodyPitch, Live2DParameterMap.Match(null, "ParamBodyAngleY"));
        Assert.Equal(Live2DSemanticParameter.BodyRoll, Live2DParameterMap.Match(null, "ParamBodyAngleZ"));
    }

    /// <summary>显示名优先:Id 猜不出含义时,靠显示名也能找回角色。</summary>
    [Fact]
    public void 显示名优先于Id()
    {
        Assert.Equal(
            Live2DSemanticParameter.HeadYaw,
            Live2DParameterMap.Match("Head Yaw", "weird_opaque_id_1234"));
    }

    [Theory]
    [InlineData("head_yaw", Live2DSemanticParameter.HeadYaw)]
    [InlineData("Head-Yaw", Live2DSemanticParameter.HeadYaw)]
    [InlineData("HEAD YAW", Live2DSemanticParameter.HeadYaw)]
    [InlineData("bodySway", Live2DSemanticParameter.BodySway)]
    public void 大小写与分隔符不敏感(string name, Live2DSemanticParameter expected)
    {
        Assert.Equal(expected, Live2DParameterMap.Match(name, null));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("{ not json")]
    [InlineData("{\"Parameters\": []}")]
    [InlineData("{\"Other\": 1}")]
    public void 无效输入返回空映射而不抛异常(string? json)
    {
        Live2DParameterMap map = Live2DParameterMap.FromDisplayInfoJson(json);

        Assert.Equal(0, map.Count);
        Assert.Null(map.Resolve(Live2DSemanticParameter.HeadYaw));
    }

    /// <summary>无 Id 的参数项必须跳过,不能让整份映射失败。</summary>
    [Fact]
    public void 缺少Id的参数项被跳过()
    {
        const string json = """
        {
          "Parameters": [
            { "Name": "Head Yaw" },
            { "Id": "ParamPL_param_body_sway_236bb03fd820", "Name": "Body Sway" }
          ]
        }
        """;

        Live2DParameterMap map = Live2DParameterMap.FromDisplayInfoJson(json);

        Assert.Equal(1, map.Count);
        Assert.Equal("ParamPL_param_body_sway_236bb03fd820", map.Resolve(Live2DSemanticParameter.BodySway));
    }

    /// <summary>同一角色出现多次时取先出现者,保证结果稳定。</summary>
    [Fact]
    public void 重复角色取首个()
    {
        const string json = """
        {
          "Parameters": [
            { "Id": "first_angle_x", "Name": "Head Yaw" },
            { "Id": "second_angle_x", "Name": "Head Yaw" }
          ]
        }
        """;

        Live2DParameterMap map = Live2DParameterMap.FromDisplayInfoJson(json);

        Assert.Equal("first_angle_x", map.Resolve(Live2DSemanticParameter.HeadYaw));
    }

    /// <summary>
    /// 官方 SDK 会硬编码驱动的角色,必须都有对应的官方参数名 ——
    /// 判定"该角色是否已由 SDK 驱动"靠它,漏一个就会让官方模型被重复驱动。
    /// </summary>
    [Fact]
    public void SDK驱动的角色都有官方参数名()
    {
        foreach (Live2DSemanticParameter role in Live2DParameterMap.AllRoles)
        {
            if (role is Live2DSemanticParameter.TailFollow or Live2DSemanticParameter.EarsFollow)
            {
                continue;   // 第三方导出器私有参数,官方 SDK 里没有对应物
            }

            Assert.False(
                string.IsNullOrWhiteSpace(Live2DParameterMap.OfficialId(role)),
                $"角色 {role} 缺少官方参数名");
        }
    }

    /// <summary>
    /// 尾巴/耳朵跟随是第三方导出器的私有参数,官方 SDK <b>不会</b>驱动它们 ——
    /// 返回 null 正是"必须由程序化待机接管"的判据。
    /// </summary>
    [Fact]
    public void 次级运动角色没有官方参数名()
    {
        Assert.Null(Live2DParameterMap.OfficialId(Live2DSemanticParameter.TailFollow));
        Assert.Null(Live2DParameterMap.OfficialId(Live2DSemanticParameter.EarsFollow));
    }

    /// <summary>官方参数名必须就是 SDK 硬编码写入的那几个(写错等于判定失效)。</summary>
    [Theory]
    [InlineData(Live2DSemanticParameter.HeadYaw, "ParamAngleX")]
    [InlineData(Live2DSemanticParameter.HeadPitch, "ParamAngleY")]
    [InlineData(Live2DSemanticParameter.HeadRoll, "ParamAngleZ")]
    [InlineData(Live2DSemanticParameter.BodySway, "ParamBodyAngleX")]
    [InlineData(Live2DSemanticParameter.GazeX, "ParamEyeBallX")]
    [InlineData(Live2DSemanticParameter.GazeY, "ParamEyeBallY")]
    [InlineData(Live2DSemanticParameter.Breath, "ParamBreath")]
    [InlineData(Live2DSemanticParameter.BlinkLeft, "ParamEyeLOpen")]
    [InlineData(Live2DSemanticParameter.BlinkRight, "ParamEyeROpen")]
    public void 官方参数名与SDK硬编码一致(Live2DSemanticParameter role, string expected)
    {
        Assert.Equal(expected, Live2DParameterMap.OfficialId(role));
    }

    /// <summary>官方参数名自身必须能被解析回同一角色(自洽性)。</summary>
    [Fact]
    public void 官方参数名能解析回同一角色()
    {
        foreach (Live2DSemanticParameter role in Live2DParameterMap.AllRoles)
        {
            if (Live2DParameterMap.OfficialId(role) is not { } official)
            {
                continue;   // 次级运动角色没有官方参数名,跳过
            }

            Assert.Equal(role, Live2DParameterMap.Match(null, official));
        }
    }

    /// <summary>第三方导出器的尾巴/耳朵跟随参数必须能被解析出来 —— 否则尾巴永远不会动。</summary>
    [Theory]
    [InlineData("ParamPL_param_agent_tail_follow_e332720f9a41", Live2DSemanticParameter.TailFollow)]
    [InlineData("ParamPL_param_agent_ears_follow_f650c1753e88", Live2DSemanticParameter.EarsFollow)]
    [InlineData("尾巴 Follow", Live2DSemanticParameter.TailFollow)]
    [InlineData("耳朵 Follow", Live2DSemanticParameter.EarsFollow)]
    public void 次级运动参数可解析(string idOrName, Live2DSemanticParameter expected)
    {
        Assert.Equal(expected, Live2DParameterMap.Match(idOrName, idOrName));
    }
}
