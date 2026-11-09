using CommunityToolkit.Mvvm.Messaging.Messages;
using McKuro.Services;

namespace McKuro.ViewModels;

/// <summary>导航请求消息(由 ToolkitViewModel 等发起,MainWindowViewModel 接收并切换页面)。</summary>
/// <remarks>
/// 使用 <see cref="ValueChangedMessage{T}"/> 是为了让消息是值类型,降低分配成本;
/// 携带的是 <see cref="string"/> 页面 key,而不是具体的 <see cref="NavigationItem"/>,
/// 避免持有 ViewModel 引用造成生命周期问题。
/// </remarks>
public sealed class NavigationRequestedMessage : ValueChangedMessage<string>
{
    public NavigationRequestedMessage(string pageKey) : base(pageKey)
    {
    }
}

/// <summary>已注册的导航页面 key(字符串常量,避免拼写错误)。</summary>
public static class NavigationKeys
{
    public const string Home = "Home";
    public const string Launcher = "Launcher";
    public const string Gacha = "Gacha";
    public const string Roles = "Roles";
    public const string Sign = "Sign";
    public const string Activity = "Activity";
    public const string Wiki = "Wiki";
    public const string PlayTime = "PlayTime";
    public const string Tower = "Tower";
    public const string RedeemCodes = "RedeemCodes";
    public const string Account = "Account";
    public const string Settings = "Settings";

    /// <summary>
    /// 页面 key 与「页内定位」的分隔符。塔页有三个玩法共用一个页面
    /// (深塔/矩阵/海墟各是页签),提醒卡片的「前往」必须能指定落到哪个页签,
    /// 故 key 可写成 <c>Tower#1</c>(= 终焉矩阵)。见 <see cref="ParseTowerTab"/>。
    /// </summary>
    public const char TabSeparator = '#';

    /// <summary>
    /// 拆出「页面 key」与「页内页签序号」:输入 <c>Tower#1</c> 返回 <c>("Tower", 1)</c>;
    /// 无后缀时页签为 <c>null</c>(表示保持页面当前页签不变)。
    /// <para>后缀不是非负整数时按"不带定位"处理,并把整串当页面 key 原样返回 —— 宁可跳到页面,
    /// 也不要因为一个畸形 key 变成毫无反应。</para>
    /// </summary>
    public static (string PageKey, int? TabIndex) ParseTowerTab(string? key)
    {
        if (string.IsNullOrEmpty(key))
        {
            return ("", null);
        }
        var sep = key.LastIndexOf(TabSeparator);
        if (sep <= 0 || sep == key.Length - 1)
        {
            return (key, null);
        }
        var suffix = key[(sep + 1)..];
        return int.TryParse(suffix, System.Globalization.NumberStyles.None,
            System.Globalization.CultureInfo.InvariantCulture, out var tab)
            ? (key[..sep], tab)
            : (key, null);
    }
}

/// <summary>
/// 游戏安装目录变更消息(设置页选择/修改目录后发送,启动器页接收并自动识别加载)。
/// 携带新目录路径。
/// </summary>
public sealed class GameDirectoryChangedMessage : ValueChangedMessage<string>
{
    public GameDirectoryChangedMessage(string gameRootDir) : base(gameRootDir)
    {
    }
}

/// <summary>
/// 角色数据页刷新请求消息(登录成功/切换账号后发送,RolesViewModel 接收并自动同步库街区角色养成数据)。
/// 携带账号 UserId。
/// </summary>
public sealed class RolesRefreshRequestedMessage : ValueChangedMessage<string>
{
    public RolesRefreshRequestedMessage(string userId) : base(userId)
    {
    }
}

/// <summary>
/// 游戏会话结束消息(启动页 GameProcessMonitor 检测到游戏进程退出后发送,
/// 主页接收后重拉今日数据、游玩统计页接收后重新解析日志刷新今日游玩时间)。
/// 携带结束原因(仅 Finished 时各页面才需要刷新,Failed 表示游戏从未真正运行)。
/// </summary>
public sealed class GameSessionEndedMessage : ValueChangedMessage<GameSessionEndReason>
{
    public GameSessionEndedMessage(GameSessionEndReason reason) : base(reason)
    {
    }
}
/// <summary>
/// Live2D 设置变更消息(设置页开关/导入/参数调整后发送,主页接收并即时更新/卸载模型)。
/// </summary>
public sealed class Live2DSettingsChangedMessage : ValueChangedMessage<bool>
{
    public Live2DSettingsChangedMessage(bool enabled) : base(enabled)
    {
    }
}

/// <summary>背景视频清晰度变更(设置页改档后发送,启动页据此重建播放会话)。</summary>
public sealed class BackgroundVideoQualityChangedMessage : ValueChangedMessage<int>
{
    public BackgroundVideoQualityChangedMessage(int quality) : base(quality)
    {
    }
}

/// <summary>角色页技能演示视频开关变更(关闭后不创建 mpv 解码会话)。</summary>
public sealed class RoleSkillDemoSettingsChangedMessage : ValueChangedMessage<bool>
{
    public RoleSkillDemoSettingsChangedMessage(bool enabled) : base(enabled)
    {
    }
}

/// <summary>资讯页轮播视频开关变更(关闭后不创建 mpv 解码器/内嵌 WebView)。</summary>
public sealed class WikiBannerVideoSettingsChangedMessage : ValueChangedMessage<bool>
{
    public WikiBannerVideoSettingsChangedMessage(bool enabled) : base(enabled)
    {
    }
}

/// <summary>
/// 账号头像已解析(本地磁盘缓存路径)。
/// 主页拉取每日数据后把头像落盘缓存(icon_cache/avatar,按 userId),
/// 导航栏左上角账号头像订阅此消息即时切换为真实头像。
/// </summary>
public sealed class AvatarResolvedMessage : ValueChangedMessage<string>
{
    public AvatarResolvedMessage(string localPath) : base(localPath)
    {
    }
}

/// <summary>
/// 悬浮通知(Toast)消息:携带要展示的文案,由 MainWindowViewModel 订阅并在
/// 主窗口内容区中间靠上位置显示,3 秒后自动关闭。
/// </summary>
public sealed class ShowToastMessage : ValueChangedMessage<string>
{
    public ShowToastMessage(string text) : base(text)
    {
    }
}
