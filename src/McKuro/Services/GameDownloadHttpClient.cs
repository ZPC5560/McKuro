namespace McKuro.Services;

/// <summary>
/// 游戏文件下载专用的 <see cref="HttpClient"/> 包装。
/// <para>
/// <b>为什么要单独一个类型(而不是直接用 HttpClient):</b>DI 里已经注册了一个全局
/// <see cref="HttpClient"/>(元数据用),若再注册第二个裸 <c>HttpClient</c> 会覆盖前者、
/// 把所有元数据请求也带到下载客户端的配置上。这里用一个只含单个属性的包装类型区分两者,
/// 解析时各取所需。
/// </para>
/// <para>
/// <b>与元数据客户端的差异(都是踩过的坑):</b>
/// </para>
/// <list type="bullet">
///   <item><c>AutomaticDecompression = None</c> + <c>Accept-Encoding: identity</c> ——
///     下载走 HTTP Range 续传,一旦 CDN 对 <c>206</c> 响应做了透明压缩,
///     <c>Content-Range</c> 指的就是压缩体的偏移,而解压后的字节流与清单错位。
///     现有防护只是"写入量对不上就抛异常进重试",表现为"个别 CDN 节点偶发下载失败"。</item>
///   <item><c>Timeout = InfiniteTimeSpan</c> —— 元数据客户端 60 秒总超时对下载是致命的:
///     一个大文件续传可能持续很久,总超时会在中途把连接掐掉。挂死保护由
///     <c>FileDownloader</c> 的「单次读取空闲超时」(90 秒无数据)负责。</item>
///   <item>连接池参数按下载场景调优(<c>MaxConnectionsPerServer=16</c> 高于
///     <c>DownloadEngine</c> 默认并发 8,避免连接复用成为瓶颈)。</item>
/// </list>
/// </summary>
public sealed class GameDownloadHttpClient
{
    public GameDownloadHttpClient(HttpClient client) => Client = client;

    /// <summary>下载用客户端(调用方不要改它的默认请求头/超时)。</summary>
    public HttpClient Client { get; }
}
