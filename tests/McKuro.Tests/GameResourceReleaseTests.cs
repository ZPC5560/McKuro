using McKuro.Controls;
using McKuro.Services;

namespace McKuro.Tests;

/// <summary>
/// 游戏会话资源释放的结构性回归守卫。
/// 真正的 GL/mpv 生命周期由 scripts/smoke-game-release.ps1 做带内存采样的实机验证；
/// 这里把已经踩过的四个断点固定在源码契约上，防止后续重构绕开它们。
/// </summary>
public sealed class GameResourceReleaseTests
{
    private static string RepositoryRoot()
    {
        var dir = AppContext.BaseDirectory;
        for (var i = 0; i < 8 && dir is not null; i++)
        {
            if (Directory.Exists(Path.Combine(dir, "src", "McKuro")))
            {
                return dir;
            }
            dir = Directory.GetParent(dir)?.FullName;
        }
        throw new DirectoryNotFoundException("未能定位仓库根(src/McKuro 所在目录)");
    }

    private static string Source(string relative)
        => File.ReadAllText(Path.Combine(RepositoryRoot(), relative.Replace('/', Path.DirectorySeparatorChar)));

    [Fact]
    public void 游戏启动必须缩短既有最小化释放计时器()
    {
        var source = Source("src/McKuro/Controls/VideoBackgroundControl.cs");
        var updateBody = MethodBody(source, "private void UpdateSuspendState()");
        var scheduleBody = MethodBody(source, "private void ScheduleSuspendRelease(TimeSpan delay, bool forGame = false)");

        Assert.Contains("ScheduleSuspendRelease(gameActive ? SuspendReleaseDelayGame : SuspendReleaseDelay, gameActive)", updateBody);
        Assert.Contains("forGame && !_suspendReleaseForGame", scheduleBody);
        Assert.Contains("CancelSuspendRelease();", scheduleBody);
    }

    [Fact]
    public void 游戏期间手动暂停的视频也必须释放()
    {
        var source = Source("src/McKuro/Controls/VideoBackgroundControl.cs");
        var body = MethodBody(source, "private void ScheduleSuspendRelease(TimeSpan delay, bool forGame = false)");

        Assert.Contains("if (!forGame && (_userPaused || _reachedEnd))", body);
        Assert.Contains("DisposePlayer();", body);
    }

    [Fact]
    public void GL入树前登记且迟到初始化不得跨会话重建()
    {
        var source = Source("src/McKuro/Controls/VideoBackgroundControl.cs");
        var body = MethodBody(source, "private async Task TryGlOrFallbackAsync(long gen, CancellationToken token)");
        var initBody = MethodBody(source, "protected override void OnOpenGlInit(GlInterface gl)");

        Assert.Contains("new VideoGlRenderer(this, glReadyTcs, gen)", body);
        var register = body.IndexOf("_glRenderer = gl;", StringComparison.Ordinal);
        var attach = body.IndexOf("Children.Add(gl);", StringComparison.Ordinal);
        Assert.True(register >= 0 && attach > register, "GL 必须先登记所有权再入树，初始化中的游戏释放才不会漏拆。");
        Assert.Contains("_shuttingDown || _owner.Stale(_generation)", initBody);
        Assert.Contains("!ReferenceEquals(_owner._glRenderer, this)", initBody);
    }

    [Fact]
    public void Live2D跟随游戏状态卸载并恢复且保留用户设置()
    {
        var source = Source("src/McKuro/Controls/Live2DModelHost.cs");
        var handler = MethodBody(source, "private void OnGameStateChanged(GameSessionState state)");
        var update = MethodBody(source, "private void Update()");
        var detach = MethodBody(source, "protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)");

        Assert.Contains("state != GameSessionState.Idle", handler);
        Assert.Contains("Update();", handler);
        Assert.Contains("&& !_gameActive", update);
        Assert.Contains("Teardown();", update);
        Assert.Contains("AppServices.GameMonitor.StateChanged -= OnGameStateChanged;", detach);
        Assert.DoesNotContain("Show =", handler);
    }

    [Fact]
    public void 游戏进行中不得新建播放会话()
    {
        var source = Source("src/McKuro/Controls/VideoBackgroundControl.cs");
        var body = MethodBody(source, "private void TryStartVideo()");

        // 释放后仍有多条路径回到 TryStartVideo（首帧重试/属性变化/重新挂载）。
        // 不封住这里，内存会「先降后涨回原样」—— 实测基线在游戏运行中又弹回 1.5 GB。
        var guard = body.IndexOf("AppServices.GameMonitor.State != GameSessionState.Idle", StringComparison.Ordinal);
        var createMpv = body.IndexOf("new LoopingMpvContext()", StringComparison.Ordinal);
        Assert.True(guard >= 0, "TryStartVideo 必须在游戏会话期间短路，否则释放后会被重新拉起。");
        Assert.True(createMpv < 0 || guard < createMpv, "游戏会话守卫必须早于 mpv 上下文创建。");
        Assert.Contains("_fallback!.ImageUrl = FallbackImageUrl;", body);
    }

    [Fact]
    public void 游戏期间始终释放且退出后可恢复()
    {
        var source = Source("src/McKuro/Controls/VideoBackgroundControl.cs");
        var body = MethodBody(source, "private void UpdateSuspendState()");

        // 用户选择“游戏运行期间始终释放”:还原窗口也不能重建视频。
        var guard = body.IndexOf("if (gameActive)", StringComparison.Ordinal);
        var rebuild = body.IndexOf("TryStartVideo();", StringComparison.Ordinal);
        Assert.True(guard >= 0 && rebuild > guard,
            "UpdateSuspendState 在游戏进行中必须先返回，避免还原窗口时重建播放会话。");
        // 但游戏结束后仍必须能恢复(guard 之后仍保留重建路径)
        Assert.Contains("ScheduleStartRetry();", body);
    }

    [Fact]
    public void 释放后必须交还工作集()
    {
        var video = Source("src/McKuro/Controls/VideoBackgroundControl.cs");
        var schedule = MethodBody(video, "private void ScheduleSuspendRelease(TimeSpan delay, bool forGame = false)");

        // 只 Dispose 解码器不够：页仍驻留，16GB 机器上依旧占内存。
        Assert.Contains("WorkingSetTrimmer.Trim();", schedule);

        var window = Source("src/McKuro/MainWindow.axaml.cs");
        var hook = MethodBody(window, "private void OnGameSessionStateChangedForMemory(GameSessionState state)");
        Assert.Contains("UpdateContentVisibilityForGameSession();", hook);
        var closed = window.IndexOf("StateChanged -= OnGameSessionStateChangedForMemory", StringComparison.Ordinal);
        Assert.True(closed >= 0, "窗口关闭时必须解绑游戏状态订阅，否则 MainWindow 被静态事件钉住。");
        Assert.Contains("PropertyChanged -= OnWindowStateChangedForMemory", window);
    }

    [Fact]
    public void 窗口可见时游戏会话不整块隐藏内容区()
    {
        var window = Source("src/McKuro/MainWindow.axaml.cs");
        var update = MethodBody(window, "private void UpdateContentVisibilityForGameSession()");

        // 未设置「启动后最小化」时窗口保持可见:内容区不能整块隐藏(否则用户看到一片空白窗口,
        // 视频/Live2D 的正确让位方式是各自降级到静态内容)。只有「游戏会话 + 窗口实际
        // 最小化/隐藏(托盘)」同时成立才整块隐藏内容区。
        Assert.Contains("windowHidden", update);
        var hide = update.IndexOf("active && windowHidden", StringComparison.Ordinal);
        Assert.True(hide >= 0, "内容区隐藏必须同时要求游戏会话进行中且窗口不可见。");
        Assert.Contains("WindowState == WindowState.Minimized", update);
        Assert.Contains("!IsVisible", update);
        // 用户在游戏运行中手动最小化/还原窗口时也要重算可见性
        var prop = MethodBody(window, "private void OnWindowStateChangedForMemory(object? sender, AvaloniaPropertyChangedEventArgs e)");
        Assert.Contains("WindowStateProperty", prop);
        Assert.Contains("IsVisibleProperty", prop);
        Assert.Contains("UpdateContentVisibilityForGameSession();", prop);
    }

    [Fact]
    public void 交还工作集只做一次压缩GC且失败不影响功能()
    {
        var source = Source("src/McKuro/Services/WorkingSetTrimmer.cs");

        Assert.Contains("EmptyWorkingSet", source);
        Assert.Contains("LargeObjectHeapCompactionMode", source);
        // 必须有兜底：平台不支持/调用失败时不能抛出去影响游戏启动
        Assert.Contains("catch (Exception)", source);
        // 不能周期性调用（会造成反复换页），只允许显式时机触发
        Assert.DoesNotContain("DispatcherTimer", source);
        Assert.DoesNotContain("new Thread", source);
    }

    /// <summary>按大括号配平截取方法体，避免注释或其他方法中的同名调用造成假阳性。</summary>
    private static string MethodBody(string source, string signature)
    {
        var start = source.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(start >= 0, $"找不到方法: {signature}");
        var open = source.IndexOf('{', start + signature.Length);
        Assert.True(open >= 0, $"方法没有方法体: {signature}");
        var depth = 0;
        for (var i = open; i < source.Length; i++)
        {
            if (source[i] == '{') depth++;
            else if (source[i] == '}' && --depth == 0) return source[open..(i + 1)];
        }
        throw new InvalidDataException($"方法大括号未闭合: {signature}");
    }
}
