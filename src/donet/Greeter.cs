namespace donet;

/// <summary>
/// 示例业务逻辑:简单问候语。
/// </summary>
public static class Greeter
{
    public static string Greet(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return $"Hello, {name}!";
    }
}
