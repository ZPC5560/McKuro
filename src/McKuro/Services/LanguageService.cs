using System.Reflection;
using System.Text.Json;
using Avalonia.Markup.Xaml;

namespace McKuro.Services;

/// <summary>
/// 轻量多语言服务:从内嵌 JSON 资源加载字符串(zh-Hans / en-US)。
/// 语言切换在设置页选择后保存,重启生效。
/// </summary>
public static class LanguageService
{
    private static Dictionary<string, string> _strings = new(StringComparer.Ordinal);

    public static string Current { get; private set; } = "zh-Hans";

    /// <summary>按 key 取字符串,未找到时返回 key 本身。</summary>
    public static string Get(string key)
    {
        return _strings.TryGetValue(key, out var value) ? value : key;
    }

    /// <summary>加载指定语言资源(zh-Hans / en-US),失败时回退 zh-Hans。</summary>
    public static void Load(string lang)
    {
        Current = lang is "en-US" ? "en-US" : "zh-Hans";
        var resourceName = $"McKuro.Assets.lang.{Current}.json";
        try
        {
            using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName);
            if (stream is null)
            {
                _strings = [];
                return;
            }
            using var reader = new StreamReader(stream);
            var json = reader.ReadToEnd();
            var dict = JsonSerializer.Deserialize(json, LanguageJsonContext.Default.DictionaryStringString);
            _strings = dict ?? [];
        }
        catch (Exception)
        {
            _strings = [];
        }
    }

    /// <summary>格式化字符串(key 对应模板支持 {0} 占位)。</summary>
    public static string Format(string key, params object?[] args)
    {
        var template = Get(key);
        return args.Length == 0 ? template : string.Format(template, args);
    }
}

[System.Text.Json.Serialization.JsonSerializable(typeof(Dictionary<string, string>))]
public sealed partial class LanguageJsonContext : System.Text.Json.Serialization.JsonSerializerContext;

/// <summary>
/// XAML 本地化标记扩展:{localize:Localize Key=Nav.Home}
/// 语言切换需重启应用后生效。
/// </summary>
public sealed class LocalizeExtension : MarkupExtension
{
    public string? Key { get; set; }

    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        return LanguageService.Get(Key ?? "");
    }
}
