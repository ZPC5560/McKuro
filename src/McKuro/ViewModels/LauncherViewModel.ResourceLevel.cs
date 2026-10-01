using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using McKuro.Core.Services.Game;
using McKuro.Services;

namespace McKuro.ViewModels;

/// <summary>
/// 启动器页「选择资源等级」(对标官方启动器的画质包切换):
/// 列出极致/高清/流畅及各自体积与是否已安装;切换到未安装的等级时需二次确认,
/// 确认后按等级差异下载该画质包并直接安装到游戏目录。
/// </summary>
public sealed partial class LauncherViewModel
{
    /// <summary>资源等级选项(官方样式的卡片列表)。</summary>
    public ObservableCollection<ResourceLevelItem> ResourceLevelItems { get; } = [];

    /// <summary>是否已具备展示条件(渠道支持 + 已装游戏 + 有可选项);由 <see cref="RefreshResourceLevelsAsync"/> 维护。</summary>
    private bool _resourceLevelPanelReady;

    /// <summary>
    /// 小按钮(如「高清 ▾」)是否可见。
    /// 对齐官方启动器:默认只显示这个胶囊按钮,点击后才弹出等级卡片列表。
    /// 与下载进度卡同处一列,故下载/切换期间必须隐藏,避免两块面板叠在一起。
    /// </summary>
    public bool ShowResourceLevelPill => _resourceLevelPanelReady && !IsDownloading && !IsSwitchingResourceLevel;

    /// <summary>用户是否已点开等级卡片列表(官方启动器的展开态)。</summary>
    [ObservableProperty]
    private bool _isResourceLevelPickerOpen;

    /// <summary>等级卡片列表是否可见(点小按钮后展开;官方样式默认收起)。</summary>
    public bool ShowResourceLevelPanel => ShowResourceLevelPill && IsResourceLevelPickerOpen;

    partial void OnIsResourceLevelPickerOpenChanged(bool value) =>
        OnPropertyChanged(nameof(ShowResourceLevelPanel));

    /// <summary>展开/收起等级卡片列表(点击「高清 ▾」小按钮)。</summary>
    [RelayCommand]
    private void ToggleResourceLevelPicker()
    {
        if (!ShowResourceLevelPill)
        {
            return;
        }
        IsResourceLevelPickerOpen = !IsResourceLevelPickerOpen;
    }

    private void SetResourceLevelPanelReady(bool ready)
    {
        if (_resourceLevelPanelReady == ready)
        {
            return;
        }
        _resourceLevelPanelReady = ready;
        if (!ready)
        {
            // 数据不可用时收起,避免留下无法操作的空面板
            IsResourceLevelPickerOpen = false;
        }
        OnPropertyChanged(nameof(ShowResourceLevelPill));
        OnPropertyChanged(nameof(ShowResourceLevelPanel));
    }

    /// <summary>等级列表是否正在加载。</summary>
    [ObservableProperty]
    private bool _isLoadingResourceLevels;

    /// <summary>是否正在切换/下载画质包。</summary>
    [ObservableProperty]
    private bool _isSwitchingResourceLevel;

    /// <summary>当前生效的资源等级取值(uhd/hd/sd)。</summary>
    [ObservableProperty]
    private string _currentResourceLevel = LaunchArguments.DefaultResourceLevel;

    /// <summary>当前等级的显示名(如「高清」)。</summary>
    [ObservableProperty]
    private string _currentResourceLevelText = "";

    /// <summary>渠道是否支持资源等级(仅国服官方;其它渠道隐藏面板而不是给必失败按钮)。</summary>
    public bool ResourceLevelSupported => ResourceLevelService.SupportsResourceLevels(ServerType);

    /// <summary>当前是否可交互(未在下载/切换中)。</summary>
    public bool ResourceLevelEnabled => !IsBusy && !IsDownloading && !IsSwitchingResourceLevel;

    partial void OnIsSwitchingResourceLevelChanged(bool value)
    {
        OnPropertyChanged(nameof(ResourceLevelEnabled));
        OnPropertyChanged(nameof(IsResourceLevelLocked));
        OnPropertyChanged(nameof(ShowResourceLevelPill));
        OnPropertyChanged(nameof(ShowResourceLevelPanel));
    }

    /// <summary>切换期间锁定其它操作(避免同时下载导致计费/磁盘混乱)。</summary>
    public bool IsResourceLevelLocked => IsSwitchingResourceLevel;

    /// <summary>
    /// 刷新资源等级列表(进入启动页/切换渠道/安装完成后调用)。
    /// </summary>
    public async Task RefreshResourceLevelsAsync()
    {
        if (!NativeGameManagementSupported || !IsInstalled || !ResourceLevelSupported)
        {
            SetResourceLevelPanelReady(false);
            return;
        }

        IsLoadingResourceLevels = true;
        try
        {
            var options = await AppServices.ResourceLevels.GetLevelOptionsAsync(ServerType);
            ResourceLevelItems.Clear();
            foreach (var o in options)
            {
                // 纯体积文案(如 "99.45 GB")供二次确认弹窗插入句子;
                // 卡片则显示带前缀的 "游戏大小: 99.45 GB"。
                var sizeOnly = o.TotalBytes > 0 ? FormatSize(o.TotalBytes) : "";
                ResourceLevelItems.Add(new ResourceLevelItem
                {
                    Value = o.Value,
                    BundleName = o.BundleName,
                    DisplayName = LevelDisplayName(o.Level),
                    SizeText = sizeOnly.Length > 0
                        ? LanguageService.Format("Launcher.LevelSize", sizeOnly)
                        : "",
                    SizeOnlyText = sizeOnly,
                    Installed = o.Installed,
                    IsCurrent = string.Equals(o.Value, CurrentResourceLevel, StringComparison.OrdinalIgnoreCase),
                });
            }

            // 无可用等级(端点不可用/解析失败)时不显示空面板
            SetResourceLevelPanelReady(ResourceLevelItems.Count > 0);
            SyncCurrentLevelText();
        }
        catch (Exception)
        {
            // 等级列表属增强信息,失败不影响启动/更新主流程
            SetResourceLevelPanelReady(false);
        }
        finally
        {
            IsLoadingResourceLevels = false;
        }
    }

    /// <summary>同步当前等级显示(优先按设置,其次按已安装标记推断)。</summary>
    private void SyncCurrentLevelText()
    {
        var setting = AppServices.Settings.Current.ResourceLevel;
        var effective = string.IsNullOrWhiteSpace(setting)
            ? ResourceLevelItems.FirstOrDefault(i => i.Installed)?.Value
            : setting;

        if (!string.IsNullOrWhiteSpace(effective))
        {
            CurrentResourceLevel = LaunchArguments.Normalize(effective);
        }

        var match = ResourceLevelItems.FirstOrDefault(i =>
            string.Equals(i.Value, CurrentResourceLevel, StringComparison.OrdinalIgnoreCase));
        CurrentResourceLevelText = match?.DisplayName ?? LevelDisplayName(LaunchArguments.FromValue(CurrentResourceLevel));

        foreach (var item in ResourceLevelItems)
        {
            item.IsCurrent = string.Equals(item.Value, CurrentResourceLevel, StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>
    /// 等级显示名(启动页用):只显示「高清」这样的纯名称,不带 (hd) 后缀 —— 与官方启动器
    /// 的胶囊/卡片一致。设置页的下拉仍用带后缀的完整名以便区分参数取值。
    /// </summary>
    private static string LevelDisplayName(GameResourceLevel level) => level switch
    {
        GameResourceLevel.Ultra => LanguageService.Format("Launch.ResourceLevel.Ultra.Short"),
        GameResourceLevel.Smooth => LanguageService.Format("Launch.ResourceLevel.Smooth.Short"),
        _ => LanguageService.Format("Launch.ResourceLevel.High.Short"),
    };

    /// <summary>
    /// 选择某资源等级。已安装 → 直接生效;未安装 → 抛出确认请求由 View 弹二次确认
    /// (防误点:切换可能需要下载几十 GB)。
    /// </summary>
    [RelayCommand]
    private async Task SelectResourceLevelAsync(ResourceLevelItem? item)
    {
        if (item is null || IsBusy || IsDownloading || IsSwitchingResourceLevel)
        {
            return;
        }

        if (string.Equals(item.Value, CurrentResourceLevel, StringComparison.OrdinalIgnoreCase) && item.Installed)
        {
            return;
        }

        // 已安装:无需下载,直接切换生效
        if (item.Installed)
        {
            CurrentResourceLevel = item.Value;
            AppServices.Settings.Current.ResourceLevel = item.Value;
            AppServices.Settings.Save();
            SyncCurrentLevelText();
            IsResourceLevelPickerOpen = false;
            StatusText = LanguageService.Format("Launcher.LevelSwitched", item.DisplayName);
            return;
        }

        // 未安装:交由 View 做二次确认(含体积提示);未注入时拒绝,避免静默下载几十 GB
        if (ConfirmLevelSwitchAsync is null)
        {
            StatusText = LanguageService.Format("Launcher.LevelNeedConfirm", item.DisplayName, item.SizeText);
            return;
        }

        var confirmed = await ConfirmLevelSwitchAsync(item);
        if (!confirmed)
        {
            return;
        }

        await SwitchResourceLevelAsync(item);
    }

    /// <summary>
    /// 二次确认钩子:由 View(LauncherView)注入实际弹窗,参数为待切换的等级项,返回是否确认。
    /// 未注入时视为拒绝(不静默下载几十 GB)。
    /// </summary>
    public Func<ResourceLevelItem, Task<bool>>? ConfirmLevelSwitchAsync { get; set; }

    /// <summary>执行等级切换(下载并安装该画质包)。</summary>
    private async Task SwitchResourceLevelAsync(ResourceLevelItem item)
    {
        IsSwitchingResourceLevel = true;
        IsDownloading = true;
        CanPauseCurrentPhase = false;
        ProgressPercent = 0;
        ProgressText = LanguageService.Format("Launcher.LevelDownloading", item.DisplayName);
        StatusText = LanguageService.Format("Launcher.LevelDownloadingStatus", item.DisplayName);

        var progress = new Progress<Core.Services.Game.DownloadProgress>(p => HandleProgress(p));

        try
        {
            var result = await AppServices.ResourceLevels.SwitchAsync(ServerType, item.Value, progress);
            if (result.Success)
            {
                CurrentResourceLevel = item.Value;
                StatusText = result.AlreadyInstalled
                    ? LanguageService.Format("Launcher.LevelReady", item.DisplayName)
                    : LanguageService.Format("Launcher.LevelInstalled", item.DisplayName);
            }
            else
            {
                StatusText = result.Message ?? LanguageService.Format("Launcher.LevelFailed");
            }
        }
        catch (Exception ex)
        {
            StatusText = LanguageService.Format("Launcher.LevelFailedWith", ex.Message);
        }
        finally
        {
            IsSwitchingResourceLevel = false;
            IsDownloading = false;
            ProgressText = "";
            ProgressPercent = 0;
            await RefreshResourceLevelsAsync();
            RefreshState();
            // 等级变化会影响启动参数(-krqlv),同步一次启动命令行显示
            OnPropertyChanged(nameof(CurrentResourceLevelText));
        }
    }
}

/// <summary>单个资源等级条目(官方样式卡片:名称 + 体积 + 已安装标记)。</summary>
public sealed partial class ResourceLevelItem : ObservableObject
{
    public required string Value { get; init; }

    public required string BundleName { get; init; }

    public required string DisplayName { get; init; }

    /// <summary>如「游戏大小: 80.52 GB」(卡片展示用)。</summary>
    public required string SizeText { get; init; }

    /// <summary>纯体积如「80.52 GB」(二次确认弹窗插入句子用,避免出现"游戏大小:"前缀)。</summary>
    public string SizeOnlyText { get; init; } = "";

    /// <summary>是否已安装(显示「已安装」并作为当前项高亮)。</summary>
    public bool Installed { get; init; }

    private bool _isCurrent;

    /// <summary>是否为当前生效等级(高亮边框)。</summary>
    public bool IsCurrent
    {
        get => _isCurrent;
        set => SetProperty(ref _isCurrent, value);
    }

    /// <summary>是否显示「已安装」徽标。</summary>
    public bool ShowInstalledBadge => Installed;
}
