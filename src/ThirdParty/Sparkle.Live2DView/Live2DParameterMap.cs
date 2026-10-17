using System.Text.Json;

namespace Sparkle.Live2DView;

/// <summary>模型参数在「语义角色」上的抽象。第三方导出器不保证参数 Id 用官方命名。</summary>
public enum Live2DSemanticParameter
{
    /// <summary>头部左右转动(官方 <c>ParamAngleX</c>)。</summary>
    HeadYaw,

    /// <summary>头部俯仰(官方 <c>ParamAngleY</c>)。</summary>
    HeadPitch,

    /// <summary>头部侧倾(官方 <c>ParamAngleZ</c>)。</summary>
    HeadRoll,

    /// <summary>身体左右摇摆(官方 <c>ParamBodyAngleX</c>)。</summary>
    BodySway,

    /// <summary>身体前后俯仰。</summary>
    BodyPitch,

    /// <summary>身体侧倾。</summary>
    BodyRoll,

    /// <summary>视线左右(官方 <c>ParamEyeBallX</c>)。</summary>
    GazeX,

    /// <summary>视线上下(官方 <c>ParamEyeBallY</c>)。</summary>
    GazeY,

    /// <summary>呼吸(官方 <c>ParamBreath</c>)。</summary>
    Breath,

    /// <summary>左眼开合(官方 <c>ParamEyeLOpen</c>)。</summary>
    BlinkLeft,

    /// <summary>右眼开合(官方 <c>ParamEyeROpen</c>)。</summary>
    BlinkRight,

    /// <summary>嘴部开合(官方 <c>ParamMouthOpenY</c>)。</summary>
    MouthOpen,

    /// <summary>
    /// 尾巴跟随。第三方「分层 PSD → Cubism」导出器会额外生成这类<b>次级运动</b>参数
    /// (由 physics3.json 的 Input→Output 链驱动)。它不在官方参数表里,但对"尾巴在动"是必需的。
    /// </summary>
    TailFollow,

    /// <summary>耳朵跟随(同上,第三方导出器的次级运动参数)。</summary>
    EarsFollow,
}

/// <summary>
/// 把「语义角色」解析到模型真实参数 Id。
///
/// <para><b>为什么需要</b>:Live2D 官方 SDK 与大多数播放器都把「头部转动 / 身体摇摆 / 呼吸 /
/// 视线」<b>硬编码</b>成官方参数名 <c>ParamAngleX</c>、<c>ParamBodyAngleX</c>、<c>ParamBreath</c>、
/// <c>ParamEyeBallX</c> 等。第三方「分层 PSD → Cubism」导出器(如 PuppetLoom)会按自己的规则
/// 生成参数名,例如 <c>ParamPL_param_head_yaw_80184865e1e5</c>。两边对不上时,
/// 写进去的值会落到 SDK 的「不存在的参数」旁路表里(<c>CubismModel.GetParameterIndex</c> 对未知
/// Id 返回一个合成下标,值只存在 <c>_notExistParameterValues</c>),
/// <b>完全不影响模型</b> —— 表现为:指针跟随失效、呼吸失效、整个角色静止不动。</para>
///
/// <para>本类从模型的 <c>*.cdi3.json</c>(<c>FileReferences.DisplayInfo</c>)读取每个参数的
/// <c>Name</c>/<c>Id</c>,用<b>显示名</b>做语义匹配,从而在任意命名规则下都能找回这些角色。
/// 显示名缺失时退化为直接匹配官方 Id,因此对 Hiyori 等官方模型行为不变。</para>
/// </summary>
public sealed class Live2DParameterMap
{
    /// <summary>没解析到任何角色时的空映射(调用方据此跳过,不做任何改写)。</summary>
    public static Live2DParameterMap Empty { get; } = new([]);

    private readonly Dictionary<Live2DSemanticParameter, string> _ids;

    private Live2DParameterMap(Dictionary<Live2DSemanticParameter, string> ids) => _ids = ids;

    /// <summary>解析出的角色数量。</summary>
    public int Count => _ids.Count;

    /// <summary>已解析的角色及其参数 Id。</summary>
    public IReadOnlyDictionary<Live2DSemanticParameter, string> Resolved => _ids;

    /// <summary>取某角色对应的参数 Id;未解析到返回 <c>null</c>。</summary>
    public string? Resolve(Live2DSemanticParameter role) =>
        _ids.TryGetValue(role, out string? id) ? id : null;

    /// <summary>取某角色对应的参数 Id。</summary>
    public bool TryResolve(Live2DSemanticParameter role, out string id) =>
        _ids.TryGetValue(role, out id!);

    /// <summary>
    /// 该角色在<b>官方 SDK 里被硬编码写入</b>的参数名。
    ///
    /// <para>用途:判断"这个角色是否已经由 SDK 自己驱动了"。<c>LAppModel.Update()</c> 每帧都会写
    /// <c>ParamAngleX/Y/Z</c>、<c>ParamBodyAngleX</c>、<c>ParamEyeBallX/Y</c>,
    /// <c>CubismBreath</c> 写 <c>ParamBreath</c>,<c>CubismEyeBlink</c> 写 model3.json 的
    /// <c>EyeBlink</c> 组。若模型的参数名与这些一致,SDK 的动效本来就生效,不该再叠加程序化驱动;
    /// 若不一致(第三方导出),SDK 的写入会落进"不存在参数"旁路表,必须由我们接管。</para>
    /// </summary>
    public static string? OfficialId(Live2DSemanticParameter role) => role switch
    {
        Live2DSemanticParameter.HeadYaw => "ParamAngleX",
        Live2DSemanticParameter.HeadPitch => "ParamAngleY",
        Live2DSemanticParameter.HeadRoll => "ParamAngleZ",
        Live2DSemanticParameter.BodySway => "ParamBodyAngleX",
        Live2DSemanticParameter.BodyPitch => "ParamBodyAngleY",
        Live2DSemanticParameter.BodyRoll => "ParamBodyAngleZ",
        Live2DSemanticParameter.GazeX => "ParamEyeBallX",
        Live2DSemanticParameter.GazeY => "ParamEyeBallY",
        Live2DSemanticParameter.Breath => "ParamBreath",
        Live2DSemanticParameter.BlinkLeft => "ParamEyeLOpen",
        Live2DSemanticParameter.BlinkRight => "ParamEyeROpen",
        Live2DSemanticParameter.MouthOpen => "ParamMouthOpenY",
        // 尾巴/耳朵跟随是第三方导出器的私有参数,官方 SDK 里没有对应物 ——
        // 返回 null 表示"SDK 不会驱动它",因此必须由程序化待机接管。
        _ => null,
    };

    /// <summary>所有语义角色。</summary>
    public static IReadOnlyList<Live2DSemanticParameter> AllRoles { get; } =
        Enum.GetValues<Live2DSemanticParameter>();

    /// <summary>解析 <c>*.cdi3.json</c> 文本。内容缺失/畸形时返回 <see cref="Empty"/>,不抛异常。</summary>
    public static Live2DParameterMap FromDisplayInfoJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return Empty;
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(json);
            if (!TryGetProperty(document.RootElement, "Parameters", out JsonElement parameters)
                || parameters.ValueKind != JsonValueKind.Array)
            {
                return Empty;
            }

            var ids = new Dictionary<Live2DSemanticParameter, string>();
            foreach (JsonElement parameter in parameters.EnumerateArray())
            {
                if (parameter.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                if (!TryGetProperty(parameter, "Id", out JsonElement idElement)
                    || idElement.GetString() is not { Length: > 0 } id)
                {
                    continue;
                }

                string name = TryGetProperty(parameter, "Name", out JsonElement nameElement)
                    ? nameElement.GetString() ?? string.Empty
                    : string.Empty;

                if (Match(name, id) is { } role && !ids.ContainsKey(role))
                {
                    // 同名角色只保留先出现的那个(cdi3 顺序稳定)。
                    ids[role] = id;
                }
            }

            return ids.Count == 0 ? Empty : new Live2DParameterMap(ids);
        }
        catch (JsonException)
        {
            return Empty;
        }
    }

    /// <summary>
    /// 按显示名(优先)与参数 Id(兜底)判定语义角色。大小写、空格、下划线、连字符均不敏感。
    /// </summary>
    public static Live2DSemanticParameter? Match(string? displayName, string? id)
    {
        Live2DSemanticParameter? byName = MatchToken(Normalize(displayName));
        if (byName is not null)
        {
            return byName;
        }

        return MatchToken(Normalize(id));
    }

    private static Live2DSemanticParameter? MatchToken(string token)
    {
        if (token.Length == 0)
        {
            return null;
        }

        // 顺序有讲究:先判 "body…",否则 ParamBodyAngleX 会被 "anglex" 规则错认成头部转动。
        if (token.Contains("bodyanglex", StringComparison.Ordinal)
            || token.Contains("bodysway", StringComparison.Ordinal)
            || token.Contains("bodyshift", StringComparison.Ordinal))
        {
            return Live2DSemanticParameter.BodySway;
        }

        if (token.Contains("bodyangley", StringComparison.Ordinal)
            || token.Contains("bodypitch", StringComparison.Ordinal))
        {
            return Live2DSemanticParameter.BodyPitch;
        }

        if (token.Contains("bodyanglez", StringComparison.Ordinal)
            || token.Contains("bodyroll", StringComparison.Ordinal))
        {
            return Live2DSemanticParameter.BodyRoll;
        }

        if (token.Contains("eyeballx", StringComparison.Ordinal)
            || token.Contains("gazex", StringComparison.Ordinal)
            || token.Contains("eyex", StringComparison.Ordinal))
        {
            return Live2DSemanticParameter.GazeX;
        }

        if (token.Contains("eyebally", StringComparison.Ordinal)
            || token.Contains("gazey", StringComparison.Ordinal)
            || token.Contains("eyey", StringComparison.Ordinal))
        {
            return Live2DSemanticParameter.GazeY;
        }

        if (token.Contains("eyelopen", StringComparison.Ordinal)
            || token.Contains("blinkleft", StringComparison.Ordinal)
            || token.Contains("eyeleftopen", StringComparison.Ordinal))
        {
            return Live2DSemanticParameter.BlinkLeft;
        }

        if (token.Contains("eyeropen", StringComparison.Ordinal)
            || token.Contains("blinkright", StringComparison.Ordinal)
            || token.Contains("eyerightopen", StringComparison.Ordinal))
        {
            return Live2DSemanticParameter.BlinkRight;
        }

        if (token.Contains("mouthopeny", StringComparison.Ordinal)
            || token.Contains("mouthopen", StringComparison.Ordinal))
        {
            return Live2DSemanticParameter.MouthOpen;
        }

        if (token.Contains("anglex", StringComparison.Ordinal)
            || token.Contains("headyaw", StringComparison.Ordinal)
            || token.Contains("headturn", StringComparison.Ordinal))
        {
            return Live2DSemanticParameter.HeadYaw;
        }

        if (token.Contains("angley", StringComparison.Ordinal)
            || token.Contains("headpitch", StringComparison.Ordinal)
            || token.Contains("headnod", StringComparison.Ordinal))
        {
            return Live2DSemanticParameter.HeadPitch;
        }

        if (token.Contains("anglez", StringComparison.Ordinal)
            || token.Contains("headroll", StringComparison.Ordinal)
            || token.Contains("headtilt", StringComparison.Ordinal))
        {
            return Live2DSemanticParameter.HeadRoll;
        }

        if (token.Contains("breath", StringComparison.Ordinal))
        {
            return Live2DSemanticParameter.Breath;
        }

        // 次级运动(第三方导出器私有命名)。既要认英文 Id
        // (ParamPL_param_agent_tail_follow_xxx),也要认中文显示名(「尾巴 Follow」)。
        if (token.Contains("tailfollow", StringComparison.Ordinal)
            || token.Contains("tailsway", StringComparison.Ordinal)
            || token.Contains("尾巴", StringComparison.Ordinal))
        {
            return Live2DSemanticParameter.TailFollow;
        }

        if (token.Contains("earsfollow", StringComparison.Ordinal)
            || token.Contains("earfollow", StringComparison.Ordinal)
            || token.Contains("earsway", StringComparison.Ordinal)
            || token.Contains("耳朵", StringComparison.Ordinal))
        {
            return Live2DSemanticParameter.EarsFollow;
        }

        return null;
    }

    private static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        Span<char> buffer = value.Length <= 256 ? stackalloc char[value.Length] : new char[value.Length];
        int length = 0;
        foreach (char c in value)
        {
            if (char.IsLetterOrDigit(c))
            {
                buffer[length++] = char.ToLowerInvariant(c);
            }
        }

        return new string(buffer[..length]);
    }

    private static bool TryGetProperty(JsonElement element, string name, out JsonElement value)
    {
        if (element.ValueKind == JsonValueKind.Object
            && element.TryGetProperty(name, out value))
        {
            return true;
        }

        // 少数导出器用小写键名,做一次不敏感兜底。
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (JsonProperty property in element.EnumerateObject())
            {
                if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    value = property.Value;
                    return true;
                }
            }
        }

        value = default;
        return false;
    }
}
