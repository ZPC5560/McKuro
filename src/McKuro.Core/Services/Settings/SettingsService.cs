using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using McKuro.Core.Infrastructure;
using McKuro.Core.Models.CloudGame;
using McKuro.Core.Models.Kuro;
using McKuro.Core.Services.Game;
using Microsoft.Extensions.Logging;

namespace McKuro.Core.Services.Settings;

/// <summary>应用设置。</summary>
public sealed class AppSettings
{
    /// <summary>游戏安装目录。</summary>
    public string GameRootDir { get; set; } = "";

    /// <summary>库街区 Token(用户自行获取)。</summary>
    [JsonConverter(typeof(ProtectedStringConverter))]
    public string KujiequToken { get; set; } = "";

    /// <summary>玩家角色 ID。</summary>
    public string RoleId { get; set; } = "";

    /// <summary>服务器渠道(自动检测失败时手动指定)。</summary>
    public GameServerType ServerType { get; set; } = GameServerType.Unknown;

    /// <summary>并发下载数。</summary>
    public int DownloadConcurrency { get; set; } = 8;

    /// <summary>下载速度限制(MB/s,0 = 不限速;对齐 Haiyu LimitSpeed)。</summary>
    public int LimitSpeedMbps { get; set; }

    /// <summary>
    /// 快速校验(对齐 Haiyu 1.4.3 的 FastVerify):对带分片信息的文件只校验
    /// 「文件大小 + 若干采样分片」,跳过其余分片的读盘,加快大文件校验。
    /// <para>
    /// 采样数按分片数成比例放大(小文件全查),详见 <see cref="Game.FastVerify.SampleCountFor"/>。
    /// </para>
    /// <para>
    /// 默认关闭以保持既有行为;开启后无分片信息或采样分片缺哈希的文件仍走全文件 MD5,
    /// 且「修复游戏」路径强制全量校验。
    /// </para>
    /// </summary>
    public bool FastVerifyEnabled { get; set; }

    // ---------- 游戏启动参数(对齐 Haiyu StartGameOption) ----------

    /// <summary>使用 DX11 启动(追加 -dx11 -slno 参数)。</summary>
    public bool UseDx11 { get; set; }

    /// <summary>禁用 DLSS(存配置;与 Haiyu 一致,当前不影响启动命令行)。</summary>
    public bool DisableDlss { get; set; }

    /// <summary>
    /// 游戏资源等级(uhd/hd/sd),对应官方启动器「选择资源等级」。
    /// 决定启动参数 <c>-krqlv=&lt;值&gt;</c>;该参数是鸣潮 3.x 起必需的引导参数,
    /// 缺失时游戏会直接崩溃退出。空 = 未设置(按官方默认 hd 处理)。
    /// </summary>
    public string ResourceLevel { get; set; } = "";

    /// <summary>自定义启动参数(追加到命令行末尾;其中的 -krqlv 由 <see cref="ResourceLevel"/> 统一管理)。</summary>
    public string StartGameArguments { get; set; } = "";

    /// <summary>启动 exe 文件名(空 = 自动:Wuthering Waves.exe → Client-Win64-Shipping.exe)。</summary>
    public string StartGameExeName { get; set; } = "";

    // ---------- 自定义启动程序(mod 加载器等外部程序) ----------

    /// <summary>
    /// 启用自定义启动程序:启动游戏时改为拉起 <see cref="CustomLauncherPath"/>(通常是 mod 加载器),
    /// 并把游戏启动参数一并传给它。
    /// <para>
    /// 关闭时<b>保留</b>已选路径与参数,下次开启不用重选。
    /// 与 <see cref="StartGameExeName"/> 不同:那只能指向游戏目录内的文件,这里可以是任意路径。
    /// </para>
    /// </summary>
    public bool CustomLauncherEnabled { get; set; }

    /// <summary>自定义启动程序的绝对路径(.exe)。</summary>
    public string CustomLauncherPath { get; set; } = "";

    /// <summary>自定义启动器自己的附加参数(追加在游戏参数之后)。</summary>
    public string CustomLauncherArguments { get; set; } = "";

    /// <summary>启动游戏后最小化主窗口。</summary>
    public bool MinimizeOnLaunch { get; set; }

    /// <summary>
    /// 启动后最小化位置:Taskbar(任务栏,默认)/ Tray(系统托盘;macOS 对应 Dock/菜单栏)。
    /// 仅在 <see cref="MinimizeOnLaunch"/> 启用时生效。
    /// </summary>
    public string MinimizeLocationOnLaunch { get; set; } = "Taskbar";

    /// <summary>
    /// 游戏结束后软件窗口状态:KeepCurrent(保持原样,默认)/ ShowMainWindow(显示主窗口)/ ExitApp(自动退出软件)。
    /// </summary>
    public string AfterGameExitAction { get; set; } = "KeepCurrent";

    /// <summary>主题(light/dark)。</summary>
    public string Theme { get; set; } = "Default";

    /// <summary>启动软件后默认进入的页面:Home(主页,默认)/ Launcher(鸣潮启动页)。</summary>
    public string StartupPage { get; set; } = "Home";

    // ---------- 窗口外观:沉浸 / 自动隐藏(默认关闭,保持原生窗口体验) ----------

    /// <summary>
    /// 导航栏自动隐藏:开启后左侧 76px 导航栏滑出隐藏,鼠标贴窗口左边缘时滑回,
    /// 移开导航栏后自动收起。默认关闭。
    /// </summary>
    public bool AutoHideNavRail { get; set; }

    /// <summary>
    /// 窗口操作栏自动隐藏(沉浸无边框):开启后去掉系统标题栏与边框,改为软件内自绘操作栏
    /// (拖动 / 最小化 / 最大化 / 关闭),鼠标贴窗口顶边时滑出,移开后收起。默认关闭。
    /// <para>
    /// 该开关是「无边框沉浸」的唯一入口:关闭时窗口完全保持系统原生 chrome。
    /// </para>
    /// </summary>
    public bool AutoHideTitleBar { get; set; }

    /// <summary>背景封面视频(默认开启;无 LibVLC native 库时自动回退首帧图)。</summary>
    public bool BackgroundVideoEnabled { get; set; } = true;

    /// <summary>启动页视频来源:0=官方宣传视频(随公告接口),1=自定义动态壁纸(本地视频或 Wallpaper Engine 视频壁纸)。</summary>
    public int BackgroundVideoMode { get; set; }

    /// <summary>自定义动态壁纸视频绝对路径(BackgroundVideoMode=1 时生效;文件缺失自动回退官方视频)。</summary>
    public string CustomBackgroundVideoPath { get; set; } = "";

    /// <summary>上次扫描的 Wallpaper Engine 内容目录(记忆用,如 steamapps\workshop\content\431960)。</summary>
    public string WallpaperEngineDir { get; set; } = "";

    // ---------- 首页 Live2D 模型(仅 Windows x64;依赖 Sparkle.Live2DView + Cubism Core) ----------

    /// <summary>首页显示 Live2D 模型(需平台支持 + Cubism Core + 已导入模型)。</summary>
    public bool Live2DEnabled { get; set; }

    /// <summary>Live2D 模型文件夹绝对路径(内含 *.model3.json)。</summary>
    public string Live2DModelDir { get; set; } = "";

    /// <summary>选中的模型名(不含扩展名,如 Hiyori;对应 <模型名>.model3.json)。</summary>
    public string Live2DModelName { get; set; } = "";

    /// <summary>模型缩放(0.5~3,默认 1)。</summary>
    public float Live2DZoom { get; set; } = 1f;

    /// <summary>模型水平位置(-2~2,默认 0)。</summary>
    public float Live2DPositionX { get; set; }

    /// <summary>模型垂直位置(-2~2,默认 0)。</summary>
    public float Live2DPositionY { get; set; }

    /// <summary>模型不透明度(0~1,默认 1)。</summary>
    public float Live2DOpacity { get; set; } = 1f;

    /// <summary>模型视线跟随鼠标(指针在模型区域移动时视线/朝向跟随;默认开)。</summary>
    public bool Live2DPointerFollow { get; set; } = true;

    // ---------- 游戏修复(对齐 Haiyu 的跳过校验文件) ----------

    /// <summary>
    /// 安装/修复完成后的「重复文件自检」(默认开)。
    /// <para>
    /// 开启后每次安装或修复成功,都在后台比对游戏目录实际占用与服务端清单总量,并扫描启动器
    /// 自身留下的残留(替换前备份 <c>.McKuro_backup</c>、孤儿 <c>.part</c>、补丁/差分临时目录)。
    /// 只有"明显过大"(可回收 ≥128MB 或比清单大出 25%)时才弹窗询问是否删除,
    /// 常态安装不会被打扰。
    /// </para>
    /// <para>
    /// 用户反馈(2026-10):多次断连下载后目录里出现重复文件、体积明显超出应有总量。
    /// </para>
    /// </summary>
    public bool InstallResidueCheckEnabled { get; set; } = true;

    /// <summary>修复游戏时跳过的文件相对路径列表(如 Client/Saved/Logs/Client.log)。</summary>
    public List<string> SkipVerifyFiles { get; set; } = [];

    /// <summary>修复游戏时是否删除被跳过的文件(对齐 Haiyu verifySkilDelete;关闭则保留原文件)。</summary>
    public bool AutoSkipVerifyDelete { get; set; } = true;

    // ---------- 库街区登录与签到 ----------

    /// <summary>已保存的库街区账号列表。</summary>
    public List<KuroAccount> KuroAccounts { get; set; } = [];

    /// <summary>当前账号 UserId。</summary>
    public string CurrentKuroUserId { get; set; } = "";

    /// <summary>
    /// 首页「今日数据」绑定的<b>本地启动器登录凭证</b>(对齐 Haiyu 的 LasterSelectLocalUser):
    /// 空 = 自动(取本地凭证里第一个能拉通的角色);非空 = "库街区UID|游戏角色UID" 复合键,
    /// 固定用该凭证的 oauthCode 走 PC 启动器 SDK 拉这个角色的数据。
    /// 该角色已不在本地凭证里(登出/换绑)时自动回退「自动」模式。
    /// </summary>
    public string HomeBoundAccountId { get; set; } = "";

    /// <summary>稳定设备 ID(首次生成持久化,库街区 did 头需跨启动不变,否则触发极验风控)。</summary>
    [JsonConverter(typeof(ProtectedStringConverter))]
    public string StableDeviceId { get; set; } = "";

    /// <summary>自动游戏签到(默认开启;每次启动软件后自动执行一次,当天去重)。</summary>
    public bool AutoSignEnabled { get; set; } = true;

    /// <summary>最近一次自动游戏签到执行日期("yyyy-MM-dd";防止同一天反复重启重复执行)。</summary>
    /// <remarks>
    /// 该字段历史上同时为「自动库街区每日任务」去重;后者已移出自动调度(只能在签到页手动执行),
    /// 保留字段名是为了不破坏已有用户 settings.json 的去重状态,升级当天不会被重复签到。
    /// </remarks>
    public string LastDailyAutoRunDate { get; set; } = "";

    /// <summary>活动页已忽略临期提醒的活动 Key(标题+起止时间;版本活动刷新后仅保留仍存在的 Key)。</summary>
    public List<string> IgnoredEndingActivityIds { get; set; } = [];

    // ---------- 消息通知(悬浮提醒,设置页可按类配置) ----------

    /// <summary>
    /// 通知总开关(默认开)。设置页「通知」的二级菜单开关:关闭时一切悬浮提醒都不触发,
    /// 但下面各具体类别开关保持各自的状态,重新打开总开关即原样恢复。
    /// </summary>
    public bool NotifEnabled { get; set; } = true;

    /// <summary>活动临期提醒(版本/卡池活动结束前 3 天内)。</summary>
    public bool NotifActivityEnabled { get; set; } = true;

    /// <summary>登录状态提醒(库街区/云鸣潮/攻略站会话失效)。</summary>
    public bool NotifLoginEnabled { get; set; } = true;

    /// <summary>战歌同奏(周本)收尾提醒(本周最后一天仍有剩余次数)。</summary>
    public bool NotifWeeklyEnabled { get; set; } = true;

    /// <summary>活跃度提醒(19 点后当日活跃度仍为 0)。</summary>
    public bool NotifLivenessEnabled { get; set; } = true;

    /// <summary>
    /// 深塔/海墟/矩阵赛季临期提醒(逆境深塔 / 冥歌海墟 / 终焉矩阵结束前 3 天内且未满额完成)。
    /// <para>
    /// 三类共用一个开关:<b>一份数据、一次拉取、一个设置项</b>。它们同属"赛季制挑战",
    /// 用户想不想被提醒几乎总是同一个答案;拆三个开关只会让设置页多三行同义文案。
    /// </para>
    /// </summary>
    public bool NotifTowerEnabled { get; set; } = true;

    /// <summary>
    /// 启动时重置当天提醒台账(默认关)。
    /// <para>
    /// 提醒卡片 8 秒自动消失,首轮弹出时用户可能正在忙别的、没看清就没了;而台账保证"当天不再重复",
    /// 于是这条提醒当天就再也不会出现。开启本项后,软件每次启动清空<b>当天</b>台账,
    /// 首轮检查会把当天该弹的提醒重新弹一遍 —— 代价是每启动一次就多弹一次。
    /// </para>
    /// <para>历史日期的台账记录不受影响(它们本就已跨天失效)。</para>
    /// </summary>
    public bool NotifResetOnLaunch { get; set; }

    // ---------- mcguide 攻略站 ----------

    /// <summary>mcguide x-token(攻略站登录后服务端返回)。</summary>
    [JsonConverter(typeof(ProtectedStringConverter))]
    public string GuideToken { get; set; } = "";

    /// <summary>mcguide cUid(库街区 userId)。</summary>
    public string GuideCUid { get; set; } = "";

    /// <summary>mcguide cName(SDK 登录返回的 username)。</summary>
    public string GuideCName { get; set; } = "";

    /// <summary>mcguide 登录手机号(账号页登录表单复用与同账号判定用)。</summary>
    [JsonConverter(typeof(ProtectedStringConverter))]
    public string GuidePhone { get; set; } = "";

    /// <summary>已选玩家 ID(mcguide user/player/choose)。</summary>
    public long GuidePlayerId { get; set; }

    /// <summary>已选玩家所在服务器 ID。</summary>
    public string GuideServerId { get; set; } = "";

    // ---------- 云鸣潮(云游戏)登录会话 ----------

    /// <summary>已保存的云鸣潮账号列表(多账号;当前账号由 <see cref="CurrentCloudAccountId"/> 指定)。</summary>
    public List<CloudAccount> CloudAccounts { get; set; } = [];

    /// <summary>当前云鸣潮账号的稳定 Id(手机号可为空,不能作键)。</summary>
    [JsonConverter(typeof(ProtectedStringConverter))]
    public string CurrentCloudAccountId { get; set; } = "";

    /// <summary>云鸣潮登录数据 JSON(旧版单账号字段,加载时自动迁移到 <see cref="CloudAccounts"/>)。</summary>
    [JsonConverter(typeof(ProtectedStringConverter))]
    public string CloudLoginDataJson { get; set; } = "";

    /// <summary>云鸣潮登录账号名(旧版单账号字段,迁移后不再使用)。</summary>
    public string CloudLoginName { get; set; } = "";

    /// <summary>云鸣潮登录手机号(旧版单账号字段,迁移后不再使用)。</summary>
    [JsonConverter(typeof(ProtectedStringConverter))]
    public string CloudLoginPhone { get; set; } = "";

    // ---------- 快捷键截图 ----------

    /// <summary>截图功能开关。</summary>
    public bool CaptureEnabled { get; set; } = true;

    /// <summary>截图修饰键(Win/Ctrl/Alt/Shift)。</summary>
    public string CaptureModifierKey { get; set; } = "Win";

    /// <summary>截图按键(F8)。</summary>
    public string CaptureKey { get; set; } = "F8";

    /// <summary>截图保存目录(空 = 系统图片目录/McKuro)。</summary>
    public string ScreenCapturesDir { get; set; } = "";

    // ---------- 界面语言 ----------

    /// <summary>界面语言(zh-Hans / en-US)。</summary>
    public string Language { get; set; } = "zh-Hans";

    // ---------- 应用自更新(对齐 Haiyu UpdateAppViewModel) ----------

    /// <summary>默认自更新仓库。设置页已不提供输入框,仓库只能来自此默认值或手改 JSON。</summary>
    public const string DefaultAppUpdateRepo = "ZPC5560/McKuro";

    /// <summary>
    /// GitHub 仓库("owner/repo")。空值会被 <see cref="SettingsService"/> 的迁移回填为
    /// <see cref="DefaultAppUpdateRepo"/> —— 详见 SettingsService.MigrateAppUpdateRepo。
    /// </summary>
    public string AppUpdateRepo { get; set; } = DefaultAppUpdateRepo;

    /// <summary>跳过的应用版本(不再提示该版本更新)。</summary>
    public string SkipAppVersion { get; set; } = "";

    /// <summary>启动后自动检查应用更新(延迟静默检查;发现新版按 AutoInstall 决定直接升级或弹窗询问)。</summary>
    public bool AppUpdateAutoCheck { get; set; } = true;

    /// <summary>自动下载并安装更新(零点击:下载→替换→重启新版;关闭则弹窗由用户确认)。</summary>
    public bool AppUpdateAutoInstall { get; set; }

    /// <summary>
    /// GitHub IP 域前置(默认关闭)。开启后绕过系统 DNS 直连内置 GitHub IP 表,
    /// 用于应对 DNS 污染/解析不可达导致的"检查更新超时"。仅在遇到解析问题时开启:
    /// IP 是固定值,上游换 IP 后会失效,此时应关闭回退系统 DNS。
    /// </summary>
    public bool AppUpdateIpFronting { get; set; }

    /// <summary>
    /// 下载加速模板(默认空 = 不加速)。须含 <c>{downloadUrl}</c> 占位符,
    /// 例:<c>https://gh-proxy.example/{downloadUrl}</c>。仅做字符串替换,不校验第三方可用性。
    /// </summary>
    public string AppUpdateAccelerator { get; set; } = "";
}

/// <summary>设置持久化服务(JSON 文件,支持原子写入与异步合并落盘)。</summary>
public sealed class SettingsService : ISettingsService
{
    private readonly string _settingsPath;
    private readonly ILogger<SettingsService> _logger;
    private readonly object _saveLock = new();
    private AppSettings _settings;

    // 版本号驱动的合并落盘:连续多次 SaveAsync 共享一个 flush 循环,最后一次写盘覆盖前面所有变更
    private long _dirtyVersion;
    private long _lastWrittenVersion;
    private Task? _pendingFlush;

    public SettingsService(string appDataDir, ILogger<SettingsService>? logger = null)
    {
        Directory.CreateDirectory(appDataDir);
        _settingsPath = Path.Combine(appDataDir, "settings.json");
        _logger = logger ?? new LoggerFactory().CreateLogger<SettingsService>();
        _settings = Load();
    }

    public AppSettings Current => _settings;

    /// <inheritdoc/>
    public void Save()
    {
        lock (_saveLock)
        {
            _dirtyVersion++;
            long target = _dirtyVersion;
            try
            {
                var json = Serialize(_settings);
                var temp = WriteTemp(_settingsPath, json);
                // 整个过程持锁:Save 写入的一定是最新内容,直接提交
                File.Move(temp, _settingsPath, overwrite: true);
                _lastWrittenVersion = Math.Max(_lastWrittenVersion, target);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "保存设置失败: {Path}", _settingsPath);
            }
        }
    }

    /// <inheritdoc/>
    /// <remarks>
    /// 合并落盘:多次调用共享一个 flush 任务,flush 循环追赶到最新版本为止;
    /// 最后一次落盘内容包含全部变更,避免高频保存时重复 I/O。
    /// </remarks>
    public Task SaveAsync(CancellationToken ct = default)
    {
        lock (_saveLock)
        {
            _dirtyVersion++;
            return _pendingFlush ??= FlushLoopAsync(ct);
        }
    }

    /// <inheritdoc/>
    public void Reload()
    {
        lock (_saveLock)
        {
            _settings = Load();
        }
    }

    private async Task FlushLoopAsync(CancellationToken ct)
    {
        try
        {
            while (true)
            {
                string json;
                long target;
                lock (_saveLock)
                {
                    if (_dirtyVersion <= _lastWrittenVersion)
                    {
                        _pendingFlush = null;
                        return;
                    }
                    target = _dirtyVersion;
                    json = Serialize(_settings);
                }

                var temp = await WriteTempAsync(_settingsPath, json, ct).ConfigureAwait(false);

                lock (_saveLock)
                {
                    // 写临时文件期间同步 Save() 可能已写入更新的版本:此时本次快照已过期,
                    // 提交会用旧内容覆盖新内容(例如刚续期的 phoneToken 丢失),只能丢弃。
                    if (target > _lastWrittenVersion)
                    {
                        File.Move(temp, _settingsPath, overwrite: true);
                        _lastWrittenVersion = target;
                    }
                    else
                    {
                        TryDelete(temp);
                    }

                    if (_dirtyVersion <= _lastWrittenVersion)
                    {
                        _pendingFlush = null;
                        return;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "保存设置(异步)失败: {Path}", _settingsPath);
            lock (_saveLock)
            {
                _pendingFlush = null;
            }
        }
    }

    /// <summary>序列化(调用方持 <c>_saveLock</c>);敏感字段加密失败时记警告(该字段以明文落盘,不丢数据)。</summary>
    private string Serialize(AppSettings settings)
    {
        SecretProtector.TakeProtectFailures();
        var json = JsonSerializer.Serialize(settings, SettingsJsonContext.Default.AppSettings);
        var failures = SecretProtector.TakeProtectFailures();
        if (failures > 0)
        {
            _logger.LogWarning("{Count} 个敏感字段加密失败,已按明文保存", failures);
        }
        return json;
    }

    /// <summary>
    /// 原子写入第一步:写临时文件并刷到磁盘(<c>Flush(true)</c>),返回临时文件路径;调用方再 <c>File.Move</c> 替换。
    /// 不先刷盘的话,断电时改名可能先于数据落盘,留下一个空/半截的 settings.json。
    /// </summary>
    private static string WriteTemp(string finalPath, string content)
    {
        var tempPath = NewTempPath(finalPath);
        try
        {
            using var fs = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            fs.Write(Encoding.UTF8.GetBytes(content));
            fs.Flush(flushToDisk: true);
        }
        catch
        {
            TryDelete(tempPath);
            throw;
        }
        return tempPath;
    }

    /// <summary><see cref="WriteTemp"/> 的异步版本。</summary>
    private static async Task<string> WriteTempAsync(string finalPath, string content, CancellationToken ct)
    {
        var tempPath = NewTempPath(finalPath);
        try
        {
            await using var fs = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                bufferSize: 4096, useAsync: true);
            await fs.WriteAsync(Encoding.UTF8.GetBytes(content), ct).ConfigureAwait(false);
            fs.Flush(flushToDisk: true);
        }
        catch
        {
            TryDelete(tempPath);
            throw;
        }
        return tempPath;
    }

    private static string NewTempPath(string finalPath)
    {
        var dir = Path.GetDirectoryName(finalPath);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }
        return finalPath + ".tmp-" + Guid.NewGuid().ToString("N");
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception)
        {
            // 临时文件残留不影响设置本身
        }
    }

    private AppSettings Load()
    {
        string json;
        try
        {
            if (!File.Exists(_settingsPath))
            {
                return new AppSettings();
            }
            json = File.ReadAllText(_settingsPath);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "读取设置失败,使用默认值: {Path}", _settingsPath);
            return new AppSettings();
        }

        try
        {
            SecretProtector.TakeUnprotectFailures();
            var loaded = JsonSerializer.Deserialize(json, SettingsJsonContext.Default.AppSettings);
            var failures = SecretProtector.TakeUnprotectFailures();
            if (failures > 0)
            {
                // 典型:settings.json 从别的电脑/别的 Windows 用户拷来,DPAPI 密文解不开。
                // 这些字段已置空(需要重新登录),其余设置照常使用。
                _logger.LogWarning("{Count} 个加密字段无法解密(设置文件可能来自其他电脑或用户),已清空,需要重新登录", failures);
            }
            if (loaded is not null)
            {
                return Migrate(loaded);
            }
            BackupCorruptFile(null);
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            // 内容本身解析不了(截断/手改坏/编码错):备份后再回退默认值
            BackupCorruptFile(ex);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "读取设置失败,使用默认值: {Path}", _settingsPath);
        }
        return new AppSettings();
    }

    /// <summary>
    /// 解析失败:先把坏文件改名为 <c>settings.json.corrupt-&lt;时间戳&gt;</c> 再回退默认值。
    /// 否则下一次 <see cref="Save"/> 会用默认值覆盖它,账号等全部设置无法找回。
    /// </summary>
    private void BackupCorruptFile(Exception? ex)
    {
        var backup = _settingsPath + ".corrupt-" + DateTime.Now.ToString("yyyyMMdd-HHmmss", System.Globalization.CultureInfo.InvariantCulture);
        try
        {
            File.Move(_settingsPath, backup, overwrite: true);
            _logger.LogWarning(ex, "设置文件解析失败,已备份为 {Backup} 并使用默认值", backup);
        }
        catch (Exception moveEx)
        {
            _logger.LogWarning(ex ?? moveEx, "设置文件解析失败,且备份失败({MoveError}),使用默认值: {Path}", moveEx.Message, _settingsPath);
        }
    }

    /// <summary>
    /// 旧配置升级:
    /// 1) 早期版本把资源等级写在自定义参数里手工填写(如 <c>-krqlv=uhd</c>),迁移到
    ///    结构化的 <see cref="AppSettings.ResourceLevel"/>,避免用户升级后画质被静默改回默认 hd;
    /// 2) 云鸣潮早期只支持单账号(CloudLoginDataJson/Name/Phone),迁移为多账号列表,
    ///    避免升级后已登录会话丢失;
    /// 3) 自更新仓库空串回填(见 <see cref="MigrateAppUpdateRepo"/>)。
    /// </summary>
    private static AppSettings Migrate(AppSettings settings)
    {
        MigrateResourceLevel(settings);
        MigrateCloudAccounts(settings);
        MigrateAppUpdateRepo(settings);
        return settings;
    }

    /// <summary>
    /// 自更新仓库空串回填。
    ///
    /// <para><b>为什么必须迁移</b>:<see cref="AppSettings.AppUpdateRepo"/> 的默认值是
    /// <c>ZPC5560/McKuro</c>,但 JSON 反序列化会用存量值覆盖默认值 —— 早期版本该字段默认
    /// 就是空串,且设置页已移除输入框(用户无法自行改回)。于是这些用户的 settings.json 里
    /// 永久留着 <c>"AppUpdateRepo": ""</c>,每次启动都命中
    /// <c>SettingsViewModel.CheckAppUpdateCoreAsync</c> 的「未配置更新仓库」分支,
    /// 自更新功能静默失效且无法自愈。</para>
    ///
    /// <para><b>只回填空,不覆盖非空</b>:手改 JSON 的用户可能有意指向自己的 fork,
    /// 非空值一律原样保留。</para>
    /// </summary>
    private static void MigrateAppUpdateRepo(AppSettings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.AppUpdateRepo))
        {
            settings.AppUpdateRepo = AppSettings.DefaultAppUpdateRepo;
        }
        else
        {
            // 容忍手改 JSON 时带入的多余空白(如 " ZPC5560/McKuro "),避免 Trim 前就去请求
            settings.AppUpdateRepo = settings.AppUpdateRepo.Trim();
        }
    }

    private static void MigrateResourceLevel(AppSettings settings)
    {
        if (!string.IsNullOrWhiteSpace(settings.ResourceLevel))
        {
            return;
        }

        var stripped = LaunchArguments.StripResourceLevel(settings.StartGameArguments, out var level);
        if (level is null)
        {
            return;
        }

        settings.ResourceLevel = level;
        settings.StartGameArguments = stripped;
    }

    /// <summary>
    /// 云鸣潮单账号字段 → 多账号列表迁移。
    /// <para>仅在"列表为空且有旧登录数据"时迁移并清空旧字段;列表已有数据时保留旧字段原样
    /// (可能是降级到旧版后又写入的会话,无条件清空会把它静默丢掉 —— 评审反馈)。</para>
    /// </summary>
    private static void MigrateCloudAccounts(AppSettings settings)
    {
        if (settings.CloudAccounts.Count == 0 && !string.IsNullOrWhiteSpace(settings.CloudLoginDataJson))
        {
            var phone = settings.CloudLoginPhone ?? "";
            var name = settings.CloudLoginName ?? "";
            settings.CloudAccounts.Add(new CloudAccount
            {
                // 稳定 Id:手机号优先,空则账号名,再空则 GUID(旧数据可能两者皆缺)
                Id = phone.Length > 0 ? phone : (name.Length > 0 ? name : System.Guid.NewGuid().ToString("N")),
                Phone = phone,
                Name = name,
                LoginDataJson = settings.CloudLoginDataJson,
            });
            settings.CloudLoginDataJson = "";
            settings.CloudLoginName = "";
            settings.CloudLoginPhone = "";
        }

        // 补齐缺失 Id(手改 JSON / 早期版本条目)
        foreach (var account in settings.CloudAccounts)
        {
            if (string.IsNullOrWhiteSpace(account.Id))
            {
                account.Id = account.Phone.Length > 0
                    ? account.Phone
                    : (account.Name.Length > 0 ? account.Name : System.Guid.NewGuid().ToString("N"));
            }
        }

        // 当前指针失效(空/指向不存在的 Id)时回退第一个账号
        if (string.IsNullOrWhiteSpace(settings.CurrentCloudAccountId)
            || settings.CloudAccounts.TrueForAll(a => a.Id != settings.CurrentCloudAccountId))
        {
            settings.CurrentCloudAccountId = settings.CloudAccounts.FirstOrDefault()?.Id ?? "";
        }
    }
}

[JsonSerializable(typeof(AppSettings))]
[JsonSerializable(typeof(KuroAccount))]
[JsonSerializable(typeof(List<KuroAccount>))]
[JsonSerializable(typeof(CloudAccount))]
[JsonSerializable(typeof(List<CloudAccount>))]
public sealed partial class SettingsJsonContext : JsonSerializerContext;
