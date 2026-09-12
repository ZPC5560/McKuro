using McKuro.Core.Services;

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

    /// <summary>卡池类型显示名(经 CoreStrings 本地化;未注册时回退中文)。</summary>
    public static string GetDisplayName(CardPoolType type) => type switch
    {
        CardPoolType.RoleActivity => CoreStrings.T("Gacha.Pool.RoleActivity", "角色活动"),
        CardPoolType.WeaponsActivity => CoreStrings.T("Gacha.Pool.WeaponsActivity", "武器活动"),
        CardPoolType.RoleResident => CoreStrings.T("Gacha.Pool.RoleResident", "角色常驻"),
        CardPoolType.WeaponsResident => CoreStrings.T("Gacha.Pool.WeaponsResident", "武器常驻"),
        CardPoolType.Beginner => CoreStrings.T("Gacha.Pool.Beginner", "新手唤取"),
        CardPoolType.BeginnerChoice => CoreStrings.T("Gacha.Pool.BeginnerChoice", "新手自选"),
        CardPoolType.GratitudeOrientation => CoreStrings.T("Gacha.Pool.GratitudeOrientation", "感恩定向"),
        CardPoolType.CharacterNovice => CoreStrings.T("Gacha.Pool.CharacterNovice", "角色新旅"),
        CardPoolType.WeaponNovice => CoreStrings.T("Gacha.Pool.WeaponNovice", "武器新旅"),
        CardPoolType.CharacterCollaboration => CoreStrings.T("Gacha.Pool.CharacterCollaboration", "角色联动"),
        CardPoolType.WeaponCollaboration => CoreStrings.T("Gacha.Pool.WeaponCollaboration", "武器联动"),
        CardPoolType.CharacterMemoryJourney => CoreStrings.T("Gacha.Pool.CharacterMemoryJourney", "角色忆旅"),
        CardPoolType.WeaponMemoryJourney => CoreStrings.T("Gacha.Pool.WeaponMemoryJourney", "武器忆旅"),
        _ => type.ToString(),
    };
}
