using System.Text.RegularExpressions;

namespace McKuro.Tests;

/// <summary>
/// ViewModel 异步 / 账号状态修复的源码级守卫(这些 VM 依赖静态 AppServices 与 Avalonia 调度器,
/// 无法在单测里隔离构造,故按 <see cref="ClassHandlerHygieneTests"/> 的做法断言源码)。
/// <para>每条断言对应 2026-10 代码审核的一个发现,失败信息给出编号,便于回查原因。</para>
/// </summary>
public class ViewModelAsyncGuardTests
{
    /// <summary>定位仓库根(同时校验 ViewModels 与 Views,避免命中 bin 下的同名目录而读到空文件)。</summary>
    private static string RepositoryRoot()
    {
        var dir = AppContext.BaseDirectory;
        for (var i = 0; i < 10 && dir is not null; i++)
        {
            if (Directory.Exists(Path.Combine(dir, "src", "McKuro", "ViewModels"))
                && Directory.Exists(Path.Combine(dir, "src", "McKuro", "Views")))
            {
                return dir;
            }
            dir = Directory.GetParent(dir)?.FullName;
        }
        throw new DirectoryNotFoundException($"未能定位仓库根。起点: {AppContext.BaseDirectory}");
    }

    private static string ViewModelSource(string fileName)
        => File.ReadAllText(Path.Combine(RepositoryRoot(), "src", "McKuro", "ViewModels", fileName));

    /// <summary>去掉 // 行注释,避免注释里提到的旧写法让断言误判。</summary>
    private static string StripLineComments(string source)
        => Regex.Replace(source, @"//[^\r\n]*", "");

    [Fact]
    public void SignPage_NeverRemovesAccounts()
    {
        // H4:签到页曾在任何非 200 响应时调用 KuroAccounts.Remove 永久删除账号
        var code = StripLineComments(ViewModelSource("SignViewModel.cs"));
        Assert.DoesNotContain("KuroAccounts.Remove", code);
        Assert.Contains("AccountLoginStateLogic.ProbeKuroSession", code);
    }

    [Fact]
    public void WikiPage_DoesNotLeaveUiThreadBeforeFillingCollections()
    {
        // H7:WhenAll 上的 ConfigureAwait(false) 让后续 Fill 在线程池上改 ObservableCollection
        var code = StripLineComments(ViewModelSource("WikiViewModel.cs"));
        Assert.DoesNotMatch(@"Task\.WhenAll\(newsTask,\s*annTask,\s*actTask\)\.ConfigureAwait\(false\)", code);
    }

    [Theory]
    [InlineData("WikiViewModel.cs")]
    [InlineData("AccountViewModel.cs")]
    public void RemoteUrls_GoThroughSafeUrl(string fileName)
    {
        // M32:远程链接不得直接交给 Process.Start(UseShellExecute)
        var code = StripLineComments(ViewModelSource(fileName));
        Assert.DoesNotContain("Process.Start", code);
        Assert.Contains("SafeUrl.TryOpen", code);
    }

    [Fact]
    public void Toast_AutoHide_StaysOnUiThread()
    {
        // M26:ConfigureAwait(false) 后在线程池上清空绑定属性 ToastText
        var code = StripLineComments(ViewModelSource("MainWindowViewModel.cs"));
        Assert.DoesNotMatch(@"Task\.Delay\(TimeSpan\.FromSeconds\(3\),\s*token\)\.ConfigureAwait\(false\)", code);
        Assert.Contains("token.IsCancellationRequested", code);
    }

    [Fact]
    public void AutoInstall_RequiresChecksum()
    {
        // M8:无摘要的更新包不得静默自动安装
        var code = StripLineComments(ViewModelSource("MainWindowViewModel.cs"));
        Assert.Matches(@"AppUpdateAutoInstall\s*&&\s*_settings\.PendingUpdateHasChecksum", code);
    }

    [Fact]
    public void GachaPage_HasNoUnboundAllRecordsQuery()
    {
        // M13:RefreshDetail 每次切换卡池都查一次没有任何视图绑定的 AllRecords
        var code = StripLineComments(ViewModelSource("GachaViewModel.cs"));
        Assert.DoesNotMatch(@"(?<!Get)\bAllRecords\b", code);
        // L8:记录时间是服务器本地墙上时间,不得按 UTC 解析
        Assert.DoesNotContain("AssumeUniversal", code);
    }

    [Fact]
    public void AccountPage_DoesNotLogFullMobile()
    {
        // L19:极验取消/超时日志曾记录完整手机号
        var code = StripLineComments(ViewModelSource("AccountViewModel.cs"));
        Assert.DoesNotMatch(@"LogWarning\([^;]*\{Mobile\}[^;]*,\s*mobile\)", code);
    }
}
