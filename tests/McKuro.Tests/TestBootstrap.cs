namespace McKuro.Tests;

/// <summary>
/// 测试程序集初始化:加载中文语言资源,使断言的界面文案(zh 回退文本)与
/// LanguageService 未初始化时返回 key 本身的约定解耦。
/// </summary>
internal static class TestBootstrap
{
    [global::System.Runtime.CompilerServices.ModuleInitializer]
    internal static void Init()
    {
        McKuro.Services.LanguageService.Load("zh-Hans");
    }
}
