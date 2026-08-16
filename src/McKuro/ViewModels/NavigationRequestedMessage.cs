using CommunityToolkit.Mvvm.Messaging.Messages;

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