using System.Text.Json;
using System.Text.Json.Serialization;
using McKuro.Core.Infrastructure;
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
    public string KujiequToken { get; set; } = "";

    /// <summary>玩家角色 ID。</summary>
    public string RoleId { get; set; } = "";

    /// <summary>服务器渠道(自动检测失败时手动指定)。</summary>
    public GameServerType ServerType { get; set; } = GameServerType.Unknown;

    /// <summary>并发下载数。</summary>
    public int DownloadConcurrency { get; set; } = 8;

    /// <summary>下载速度限制(MB/s,0 = 不限速;对齐 Haiyu LimitSpeed)。</summary>
    public int LimitSpeedMbps { get; set; }

    // ---------- 游戏启动参数(对齐 Haiyu StartGameOption) ----------

    /// <summary>使用 DX11 启动(追加 -dx11 -slno 参数)。</summary>
    public bool UseDx11 { get; set; }

    /// <summary>禁用 DLSS(存配置;与 Haiyu 一致,当前不影响启动命令行)。</summary>
    public bool DisableDlss { get; set; }

    /// <summary>自定义启动参数(追加到命令行末尾)。</summary>
    public string StartGameArguments { get; set; } = "";

    /// <summary>启动 exe 文件名(空 = 自动:Wuthering Waves.exe → Client-Win64-Shipping.exe)。</summary>
    public string StartGameExeName { get; set; } = "";

    /// <summary>启动游戏后最小化主窗口。</summary>
    public bool MinimizeOnLaunch { get; set; }

    /// <summary>主题(light/dark)。</summary>
    public string Theme { get; set; } = "Default";

    /// <summary>背景封面视频(默认开启;无 LibVLC native 库时自动回退首帧图)。</summary>
    public bool BackgroundVideoEnabled { get; set; } = true;

    // ---------- 动态壁纸与玻璃主题 ----------

    /// <summary>用户选择的壁纸托管路径。空值表示使用应用默认背景。</summary>
    public string WallpaperPath { get; set; } = "";

    /// <summary>是否从壁纸自动提取应用强调色。</summary>
    public bool DynamicPaletteEnabled { get; set; } = true;

    /// <summary>玻璃效果质量：Auto / High / Low。</summary>
    public string GlassQuality { get; set; } = "Auto";

    /// <summary>壁纸铺放方式：UniformToFill / Uniform / Fill。</summary>
    public string WallpaperStretch { get; set; } = "UniformToFill";

    // ---------- 游戏修复(对齐 Haiyu 的跳过校验文件) ----------

    /// <summary>修复游戏时跳过的文件相对路径列表(如 Client/Saved/Logs/Client.log)。</summary>
    public List<string> SkipVerifyFiles { get; set; } = [];

    /// <summary>修复游戏时是否删除被跳过的文件(对齐 Haiyu verifySkilDelete;关闭则保留原文件)。</summary>
    public bool AutoSkipVerifyDelete { get; set; } = true;

    // ---------- 库街区登录与签到 ----------

    /// <summary>已保存的库街区账号列表。</summary>
    public List<KuroAccount> KuroAccounts { get; set; } = [];

    /// <summary>当前账号 UserId。</summary>
    public string CurrentKuroUserId { get; set; } = "";

    /// <summary>稳定设备 ID(首次生成持久化,库街区 did 头需跨启动不变,否则触发极验风控)。</summary>
    public string StableDeviceId { get; set; } = "";

    /// <summary>自动游戏签到(默认开启)。</summary>
    public bool AutoSignEnabled { get; set; } = true;

    /// <summary>库街区每日任务(签到+浏览+点赞+分享,默认关闭,可在签到页手动执行)。</summary>
    public bool AutoKuroClientTaskEnabled { get; set; }

    // ---------- mcguide 攻略站 ----------

    /// <summary>mcguide x-token(攻略站登录后服务端返回)。</summary>
    public string GuideToken { get; set; } = "";

    /// <summary>mcguide cUid(库街区 userId)。</summary>
    public string GuideCUid { get; set; } = "";

    /// <summary>mcguide cName(SDK 登录返回的 username)。</summary>
    public string GuideCName { get; set; } = "";

    /// <summary>已选玩家 ID(mcguide user/player/choose)。</summary>
    public long GuidePlayerId { get; set; }

    /// <summary>已选玩家所在服务器 ID。</summary>
    public string GuideServerId { get; set; } = "";

    // ---------- 云鸣潮(云游戏)登录会话 ----------

    /// <summary>云鸣潮登录数据 JSON(CloudGameLoginData 序列化,用于静默续会话拉取抽卡记录)。</summary>
    public string CloudLoginDataJson { get; set; } = "";

    /// <summary>云鸣潮登录账号名(显示用)。</summary>
    public string CloudLoginName { get; set; } = "";

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

    /// <summary>GitHub 仓库("owner/repo",空 = 禁用应用自更新)。</summary>
    public string AppUpdateRepo { get; set; } = "";

    /// <summary>跳过的应用版本(不再提示该版本更新)。</summary>
    public string SkipAppVersion { get; set; } = "";
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
                WriteAtomic(_settingsPath, JsonSerializer.Serialize(_settings, SettingsJsonContext.Default.AppSettings));
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
                    json = JsonSerializer.Serialize(_settings, SettingsJsonContext.Default.AppSettings);
                }

                await WriteAtomicAsync(_settingsPath, json, ct).ConfigureAwait(false);

                lock (_saveLock)
                {
                    _lastWrittenVersion = Math.Max(_lastWrittenVersion, target);
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

    /// <summary>原子写入:写入临时文件再 rename,避免半写状态被读到。</summary>
    private static void WriteAtomic(string finalPath, string content)
    {
        var dir = Path.GetDirectoryName(finalPath);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        var tempPath = finalPath + ".tmp-" + Guid.NewGuid().ToString("N");
        File.WriteAllText(tempPath, content);
        // AOT 友好:File.Move 内部走 MoveFileEx,可原子替换
        File.Move(tempPath, finalPath, overwrite: true);
    }

    /// <summary>异步原子写入。</summary>
    private static async Task WriteAtomicAsync(string finalPath, string content, CancellationToken ct)
    {
        var dir = Path.GetDirectoryName(finalPath);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        var tempPath = finalPath + ".tmp-" + Guid.NewGuid().ToString("N");
        await File.WriteAllTextAsync(tempPath, content, ct).ConfigureAwait(false);
        File.Move(tempPath, finalPath, overwrite: true);
    }

    private AppSettings Load()
    {
        try
        {
            if (File.Exists(_settingsPath))
            {
                var json = File.ReadAllText(_settingsPath);
                return JsonSerializer.Deserialize(json, SettingsJsonContext.Default.AppSettings) ?? new AppSettings();
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "读取设置失败,使用默认值: {Path}", _settingsPath);
        }
        return new AppSettings();
    }
}

[JsonSerializable(typeof(AppSettings))]
[JsonSerializable(typeof(KuroAccount))]
[JsonSerializable(typeof(List<KuroAccount>))]
public sealed partial class SettingsJsonContext : JsonSerializerContext;
