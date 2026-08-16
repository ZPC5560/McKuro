using Microsoft.Extensions.Logging;

namespace McKuro.Core.Infrastructure;

/// <summary>
/// 为 McKuro.Core 提供的 logger factory 工厂。
/// <para>
/// <b>AOT 兼容策略</b>:Core 不直接绑定 console/debug provider
/// (它们会引入 console formatter 反射,不利于 AOT);
/// 这里只暴露 <see cref="CreateNullLoggerFactory"/>,实际 logger factory 由 McKuro 启动器构造并注入。
/// </para>
/// </summary>
public static class LoggingBuilder
{
    /// <summary>创建一个空 logger factory —— 所有日志被吞掉,适合 Core 库内部默认行为(避免硬依赖 console/debug provider)。</summary>
    public static ILoggerFactory CreateNullLoggerFactory()
        => LoggerFactory.Create(_ => { });
}
