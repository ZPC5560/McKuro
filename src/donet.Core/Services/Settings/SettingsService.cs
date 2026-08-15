using System.Text.Json;
using System.Text.Json.Serialization;
using donet.Core.Services.Game;

namespace donet.Core.Services.Settings;

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

    /// <summary>主题(light/dark)。</summary>
    public string Theme { get; set; } = "Default";

    /// <summary>背景封面视频(默认开启;无 LibVLC native 库时自动回退首帧图)。</summary>
    public bool BackgroundVideoEnabled { get; set; } = true;
}

/// <summary>设置持久化服务(JSON 文件)。</summary>
public sealed class SettingsService
{
    private readonly string _settingsPath;
    private AppSettings _settings;

    public SettingsService(string appDataDir)
    {
        Directory.CreateDirectory(appDataDir);
        _settingsPath = Path.Combine(appDataDir, "settings.json");
        _settings = Load();
    }

    public AppSettings Current => _settings;

    public void Save()
    {
        var json = JsonSerializer.Serialize(_settings, SettingsJsonContext.Default.AppSettings);
        File.WriteAllText(_settingsPath, json);
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
        catch (Exception)
        {
            // 损坏时重置
        }
        return new AppSettings();
    }
}

[JsonSerializable(typeof(AppSettings))]
public sealed partial class SettingsJsonContext : JsonSerializerContext;
