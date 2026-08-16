namespace McKuro.Core.Models.Gacha;

/// <summary>
/// 卡池类型(参考 Haiyu 的枚举)。
/// </summary>
public enum CardPoolType : int
{
    /// <summary>角色活动</summary>
    RoleActivity = 1,

    /// <summary>武器活动</summary>
    WeaponsActivity = 2,

    /// <summary>角色常驻</summary>
    RoleResident = 3,

    /// <summary>武器常驻</summary>
    WeaponsResident = 4,

    /// <summary>新手唤取</summary>
    Beginner = 5,

    /// <summary>新手自选</summary>
    BeginnerChoice = 6,

    /// <summary>感恩定向</summary>
    GratitudeOrientation = 7,

    /// <summary>角色新旅</summary>
    CharacterNovice = 8,

    /// <summary>武器新旅</summary>
    WeaponNovice = 9,

    /// <summary>角色联动</summary>
    CharacterCollaboration = 10,

    /// <summary>武器联动</summary>
    WeaponCollaboration = 11,

    /// <summary>角色忆旅</summary>
    CharacterMemoryJourney = 12,

    /// <summary>武器忆旅</summary>
    WeaponMemoryJourney = 13,
}

public static class CardPoolTypeValues
{
    public static readonly CardPoolType[] All =
    [
        CardPoolType.RoleActivity,
        CardPoolType.WeaponsActivity,
        CardPoolType.RoleResident,
        CardPoolType.WeaponsResident,
        CardPoolType.Beginner,
        CardPoolType.BeginnerChoice,
        CardPoolType.GratitudeOrientation,
        CardPoolType.CharacterNovice,
        CardPoolType.WeaponNovice,
        CardPoolType.CharacterCollaboration,
        CardPoolType.WeaponCollaboration,
        CardPoolType.CharacterMemoryJourney,
        CardPoolType.WeaponMemoryJourney,
    ];

    /// <summary>卡池类型中文名。</summary>
    public static string GetDisplayName(CardPoolType type) => type switch
    {
        CardPoolType.RoleActivity => "角色活动",
        CardPoolType.WeaponsActivity => "武器活动",
        CardPoolType.RoleResident => "角色常驻",
        CardPoolType.WeaponsResident => "武器常驻",
        CardPoolType.Beginner => "新手唤取",
        CardPoolType.BeginnerChoice => "新手自选",
        CardPoolType.GratitudeOrientation => "感恩定向",
        CardPoolType.CharacterNovice => "角色新旅",
        CardPoolType.WeaponNovice => "武器新旅",
        CardPoolType.CharacterCollaboration => "角色联动",
        CardPoolType.WeaponCollaboration => "武器联动",
        CardPoolType.CharacterMemoryJourney => "角色忆旅",
        CardPoolType.WeaponMemoryJourney => "武器忆旅",
        _ => type.ToString(),
    };
}
