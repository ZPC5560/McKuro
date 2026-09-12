namespace McKuro.Core.Services;

/// <summary>
/// Core 层展示文案本地化网关:Core 不引用应用层,由应用启动时注册解析器
/// (App: <c>CoreStrings.Resolver = LanguageService.Get</c>),Core 内的界面文案
/// 一律经 <see cref="T"/> 取值。未注册(单元测试/设计时)返回 fallback 中文原文,
/// 保证纯函数测试无需任何初始化。
/// </summary>
public static class CoreStrings
{
    /// <summary>本地化解析器:按 key 返回当前语言文案。由应用层启动时注册。</summary>
    public static Func<string, string> Resolver { get; set; } = _ => "";

    /// <summary>当前界面语言代码("zh-Hans"/"en-US")。由应用层启动时赋值,
    /// 供 Core 服务按语言选择数据源(如国际服启动器 en / zh-Hant 内容包)。</summary>
    public static string CurrentLanguage { get; set; } = "zh-Hans";

    /// <summary>按 key 取本地化文案;未注册解析器或 key 缺失时返回 fallback。</summary>
    public static string T(string key, string fallback)
    {
        try
        {
            var value = Resolver(key);
            return string.IsNullOrEmpty(value) ? fallback : value;
        }
        catch (Exception)
        {
            return fallback;
        }
    }

    /// <summary>带参数格式化版本:取模板后 string.Format,失败返回 fallback 原文。</summary>
    public static string F(string key, string fallback, params object?[] args)
    {
        var template = T(key, fallback);
        try
        {
            return string.Format(template, args);
        }
        catch (Exception)
        {
            return fallback;
        }
    }
}
