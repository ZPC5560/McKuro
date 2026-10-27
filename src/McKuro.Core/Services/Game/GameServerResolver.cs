using McKuro.Core.Services.Settings;

namespace McKuro.Core.Services.Game;

/// <summary>
/// 「当前生效的游戏渠道」的唯一判定点。
/// <para>
/// <b>为什么要有它:</b>项目里各处原本各写一份 <c>配置优先 / 自动检测兜底</c> 的三元表达式
/// (启动页、资讯页、首页、残留自检、官方记录写入),逻辑虽然相同,但只要有一处漏写
/// 或写反,就会出现「设置里切了国际服,某个页面还在用官服」这类难查的不一致 ——
/// 用户看到的就是"区服切换没生效"。收敛到这里之后,每个消费方都只调一次。
/// </para>
/// <para>
/// <b>判定顺序:</b>设置里显式选了渠道(<see cref="GameServerType"/> 不是 <c>Unknown</c>)就用它;
/// 否则按游戏目录里的 SDK 目录自动检测(<see cref="GamePathResolver.DetectServerType"/>)。
/// 两者都判定不出来时返回 <c>Unknown</c>,由调用方按官服/通用路径兜底。
/// </para>
/// </summary>
public static class GameServerResolver
{
    /// <summary>按「设置优先、自动检测兜底」解析当前生效渠道。</summary>
    public static GameServerType Resolve(ISettingsService? settings, GamePathResolver paths)
    {
        var configured = settings?.Current.ServerType ?? GameServerType.Unknown;
        return configured != GameServerType.Unknown
            ? configured
            : paths.DetectServerType();
    }
}
