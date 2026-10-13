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

            // 顺带刷新已安装画质包列表:入口按钮的可见性取决于 HasDeletablePacks,
            // 而该值只能由这次刷新填充 —— 若只在「点开面板时」才刷,按钮就永远不会出现
            // (入口的可见性依赖只有点它才会产生的数据 = 死锁)。
            await RefreshResourcePacksAsync();
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

    // ---------- 删除画质包(回收磁盘) ----------

    /// <summary>已安装、可管理的画质包列表(供「管理画质包」面板展示)。</summary>
    public ObservableCollection<ResourcePackItem> ResourcePackItems { get; } = [];

    /// <summary>是否正在加载画质包列表。</summary>
    [ObservableProperty]
    private bool _isLoadingResourcePacks;

    /// <summary>是否正在删除画质包。</summary>
    [ObservableProperty]
    private bool _isDeletingResourcePack;

    /// <summary>「管理画质包」面板是否展开(与等级选择面板互斥,避免两块叠在一起)。</summary>
    [ObservableProperty]
    private bool _isPackManagerOpen;

    /// <summary>面板是否可见(点开 + 未在下载/切换/删除中)。</summary>
    public bool ShowPackManager => _resourceLevelPanelReady && IsPackManagerOpen
        && !IsDownloading && !IsSwitchingResourceLevel && !IsDeletingResourcePack;

    partial void OnIsPackManagerOpenChanged(bool value) => OnPropertyChanged(nameof(ShowPackManager));

    partial void OnIsDeletingResourcePackChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowPackManager));
        OnPropertyChanged(nameof(ResourceLevelEnabled));
    }

    /// <summary>是否存在可删除的包(没有可删包时隐藏入口,不给空面板)。</summary>
    public bool HasDeletablePacks => ResourcePackItems.Any(p => p.CanDelete)
        && AppServices.GameMonitor.State == GameSessionState.Idle;

    /// <summary>可回收的总空间文案(如「可回收 61.50 GB」);无则空。</summary>
    [ObservableProperty]
    private string _reclaimableText = "";

    /// <summary>打开/收起「管理画质包」面板(打开时刷新列表)。</summary>
    [RelayCommand]
    private async Task TogglePackManagerAsync()
    {
        if (!_resourceLevelPanelReady || IsDownloading || IsSwitchingResourceLevel || IsDeletingResourcePack)
        {
            return;
        }

        // 与等级选择面板互斥
        IsResourceLevelPickerOpen = false;
        IsPackManagerOpen = !IsPackManagerOpen;
        if (IsPackManagerOpen)
        {
            await RefreshResourcePacksAsync();
        }
    }

    /// <summary>刷新已安装画质包列表(含可删性与体积)。</summary>
    public async Task RefreshResourcePacksAsync()
    {
        if (!NativeGameManagementSupported || !IsInstalled || !ResourceLevelSupported)
        {
            ResourcePackItems.Clear();
            UpdateReclaimable();
            return;
        }

        IsLoadingResourcePacks = true;
        try
        {
            var packs = await AppServices.ResourceLevels.GetInstalledPacksAsync(ServerType);
            ResourcePackItems.Clear();
            foreach (var p in packs)
            {
                ResourcePackItems.Add(new ResourcePackItem
                {
                    PackName = p.PackName,
                    DisplayName = PackDisplayName(p.PackName),
                    SizeText = p.SizeBytes > 0 ? FormatSize(p.SizeBytes) : "",
                    SizeBytes = p.SizeBytes,
                    IsShared = p.IsShared,
                    CanDelete = p.CanDelete,
                    BlockReason = p.BlockReason ?? "",
                    // 共享包标注「共享」;等级包标注对应等级名
                    TagText = p.IsShared
                        ? LanguageService.Format("Launcher.PackTagShared")
                        : PackDisplayName(p.PackName),
                });
            }
        }
        catch (Exception)
        {
            // 画质包管理属增强信息,失败不影响主流程
            ResourcePackItems.Clear();
        }
        finally
        {
            IsLoadingResourcePacks = false;
            UpdateReclaimable();
        }
    }

    private void UpdateReclaimable()
    {
        var bytes = ResourcePackItems.Where(p => p.CanDelete).Sum(p => p.SizeBytes);
        ReclaimableText = bytes > 0
            ? LanguageService.Format("Launcher.PackReclaimable", FormatSize(bytes))
            : "";
        OnPropertyChanged(nameof(HasDeletablePacks));
    }

    /// <summary>画质包显示名(uhd→极致、hd→高清、sd→流畅、common→通用)。</summary>
    private static string PackDisplayName(string packName) => packName.ToLowerInvariant() switch
    {
        "uhd" => LanguageService.Format("Launch.ResourceLevel.Ultra.Short"),
        "sd" => LanguageService.Format("Launch.ResourceLevel.Smooth.Short"),
        "common" => LanguageService.Format("Launcher.PackCommon"),
        _ => LanguageService.Format("Launch.ResourceLevel.High.Short"),
    };

    /// <summary>
    /// 二次确认钩子:由 View 注入弹窗,参数为待删包项,返回是否确认。
    /// 未注入时视为拒绝(删除是不可逆操作,不静默执行)。
    /// </summary>
    public Func<ResourcePackItem, Task<bool>>? ConfirmPackDeleteAsync { get; set; }

    /// <summary>删除某个画质包(需二次确认;不可删的包直接拒绝)。</summary>
    [RelayCommand]
    private async Task DeleteResourcePackAsync(ResourcePackItem? item)
    {
        if (item is null || !item.CanDelete || IsDownloading || IsSwitchingResourceLevel || IsDeletingResourcePack)
        {
            return;
        }

        // 游戏运行中禁止删除:文件被游戏占用会删不掉,而"删一半"会让安装处于不一致状态。
        // 只在按钮可见性上挡不够 —— 面板打开后用户仍可能去启动游戏,故命令入口再挡一次。
        if (AppServices.GameMonitor.State != GameSessionState.Idle)
        {
            StatusText = LanguageService.Format("Launcher.PackDeleteGameRunning");
            return;
        }

        if (ConfirmPackDeleteAsync is null)
        {
            StatusText = LanguageService.Format("Launcher.PackDeleteNeedConfirm", item.DisplayName, item.SizeText);
            return;
        }

        var confirmed = await ConfirmPackDeleteAsync(item);
        if (!confirmed)
        {
            return;
        }

        IsDeletingResourcePack = true;
        StatusText = LanguageService.Format("Launcher.PackDeleting", item.DisplayName);
        try
        {
            var result = await AppServices.ResourceLevels.DeletePackAsync(ServerType, item.PackName);
            if (result.Success)
            {
                StatusText = result.FailedFiles > 0
                    // 有删不掉的文件(占用/权限):如实告知,不谎报"已全部删除"
                    ? LanguageService.Format("Launcher.PackDeletePartial", item.DisplayName, FormatSize(result.FreedBytes), result.FailedFiles)
                    : LanguageService.Format("Launcher.PackDeleted", item.DisplayName, FormatSize(result.FreedBytes));

                // 自动回退过等级:告知用户,并同步界面上的当前等级
                if (!string.IsNullOrWhiteSpace(result.FallbackLevel))
                {
                    CurrentResourceLevel = LaunchArguments.Normalize(result.FallbackLevel);
                    StatusText += " " + LanguageService.Format("Launcher.PackDeleteFellBack", PackDisplayName(result.FallbackLevel));
                }
            }
            else
            {
                StatusText = result.Message ?? LanguageService.Format("Launcher.PackDeleteFailed", item.DisplayName);
            }
        }
        catch (Exception ex)
        {
            StatusText = LanguageService.Format("Launcher.LevelFailedWith", ex.Message);
        }
        finally
        {
            IsDeletingResourcePack = false;
            await RefreshResourcePacksAsync();
            await RefreshResourceLevelsAsync();
            RefreshState();
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

/// <summary>
/// 单个已安装画质包(「管理画质包」面板条目:名称 + 体积 + 可否删除)。
/// </summary>
public sealed partial class ResourcePackItem : ObservableObject
{
    /// <summary>包名(uhd/hd/sd/common;删除时作为标识)。</summary>
    public required string PackName { get; init; }

    /// <summary>显示名(极致/高清/流畅/通用)。</summary>
    public required string DisplayName { get; init; }

    /// <summary>体积文案(如「61.50 GB」)。</summary>
    public required string SizeText { get; init; }

    /// <summary>体积字节数(用于汇总"可回收")。</summary>
    public long SizeBytes { get; init; }

    /// <summary>标签文案(共享包显示「共享」,等级包显示等级名)。</summary>
    public string TagText { get; init; } = "";

    /// <summary>是否为共享包(common)。</summary>
    public bool IsShared { get; init; }

    /// <summary>是否允许删除。</summary>
    public bool CanDelete { get; init; }

    /// <summary>不可删除的原因(展示用);可删时为空。</summary>
    public string BlockReason { get; init; } = "";

    /// <summary>是否展示不可删原因。</summary>
    public bool ShowBlockReason => !CanDelete && BlockReason.Length > 0;
}
