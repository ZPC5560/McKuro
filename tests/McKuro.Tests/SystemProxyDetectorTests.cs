using System.Net;
using McKuro.Core.Services.Infrastructure;

namespace McKuro.Tests;

/// <summary>macOS 系统代理解析(scutil --proxy 输出 → WebProxy)。</summary>
public class SystemProxyDetectorTests
{
    private const string ScutilEnabled =
        """
        <dictionary> {
          HTTPEnable : 1
          HTTPProxy : 127.0.0.1
          HTTPPort : 7890
          HTTPSEnable : 1
          HTTPSProxy : 127.0.0.1
          HTTPSPort : 7890
          SOCKSEnable : 1
          SOCKSProxy : 127.0.0.1
          SOCKSPort : 7890
          ExceptionsList : <array> {
          0 : localhost
          }
        }
        """;

    private const string ScutilOnlyHttp =
        """
        <dictionary> {
          HTTPEnable : 1
          HTTPProxy : 192.168.1.10
          HTTPPort : 8080
          HTTPSEnable : 0
          SOCKSEnable : 0
        }
        """;

    private const string ScutilOnlySocks =
        """
        <dictionary> {
          HTTPEnable : 0
          HTTPSEnable : 0
          SOCKSEnable : 1
          SOCKSProxy : 10.0.0.2
          SOCKSPort : 1080
        }
        """;

    private const string ScutilDisabled =
        """
        <dictionary> {
          HTTPEnable : 0
          HTTPSEnable : 0
          SOCKSEnable : 0
        }
        """;

    [Fact]
    public void ParseScutil_Prefers_Https_Proxy()
    {
        var proxy = Assert.IsType<WebProxy>(SystemProxyDetector.ParseScutil(ScutilEnabled));
        var uri = proxy.GetProxy(new Uri("https://api.kurobbs.com"))!;
        Assert.Equal("127.0.0.1", uri.Host);
        Assert.Equal(7890, uri.Port);
    }

    [Fact]
    public void ParseScutil_Falls_Back_To_Http_Proxy()
    {
        var proxy = Assert.IsType<WebProxy>(SystemProxyDetector.ParseScutil(ScutilOnlyHttp));
        var uri = proxy.GetProxy(new Uri("https://api.kurobbs.com"))!;
        Assert.Equal("192.168.1.10", uri.Host);
        Assert.Equal(8080, uri.Port);
    }

    [Fact]
    public void ParseScutil_Falls_Back_To_Socks5()
    {
        var proxy = Assert.IsType<WebProxy>(SystemProxyDetector.ParseScutil(ScutilOnlySocks));
        var uri = proxy.GetProxy(new Uri("https://api.kurobbs.com"))!;
        Assert.Equal("socks5://10.0.0.2:1080", $"{uri.Scheme}://{uri.Host}:{uri.Port}");
    }

    [Fact]
    public void ParseScutil_Disabled_Or_Garbage_Returns_Null()
    {
        Assert.Null(SystemProxyDetector.ParseScutil(ScutilDisabled));
        Assert.Null(SystemProxyDetector.ParseScutil("完全无关的输出"));
        Assert.Null(SystemProxyDetector.ParseScutil(null));
    }
}
