using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;
using McKuro.Services;
using Microsoft.Extensions.Logging;

namespace McKuro.Controls;

/// <summary>
/// 应用内嵌 WebView 控件(Windows):用系统 WebView2(Evergreen Runtime)显示极验滑块页,
/// 替代跳转系统浏览器。通过 <see cref="NativeControlHost"/> 把 WebView2 的 HWND 挂进 Avalonia 视觉树。
/// <para>
/// 创建链路:反射加载随包发布的官方托管包装器(lib_manual/netcoreapp3.0 Microsoft.Web.WebView2.Core.dll)
/// 调 CreateAsync/CreateCoreWebView2ControllerAsync/Navigate 全托管 API。
/// 之所以不用裸 P/Invoke 官方 loader:实测 .NET 10 上裸调 CreateCoreWebView2EnvironmentWithOptions
/// 要么报 FILE_NOT_FOUND/E_NOINTERFACE,要么环境"成功"但浏览器子进程永不拉起;官方托管链路在同进程同参数下正常。
/// </para>
/// <para>
/// 线程模型(Avalonia 12):Win32 窗口与 NativeControlHost 都在独立平台线程上;WebView2 COM 对象
/// 绑定创建套间——环境创建、Controller 创建、Bounds/IsVisible/Navigate 全部经 WM_APP 队列在该线程串行执行,
/// 跨线程调用会得到 RPC_E_WRONG_THREAD 或 InvalidCastException。
/// </para>
/// <para>
/// 验证结果不走 JS 桥:与 macOS 相同,极验页完成后由 GeetVerifyService 的本地 HTTP 回调(/cb)接收,
/// 本控件只负责显示。运行时缺失时 <see cref="IsSupported"/> 为 false;官方包装器不可用
/// (NativeAOT 禁用内置 COM)或浏览器子进程被安全软件静默拦截(看门狗 8 秒检测渲染子窗口)时
/// 触发 <see cref="CreationFailed"/>,调用方回退系统浏览器。
/// </para>
/// </summary>
public sealed class WebView2Control : NativeControlHost
{
    private static readonly ILogger? Log = AppServices.LoggerFactory?.CreateLogger("McKuro.WebView2");

    public static readonly StyledProperty<string> UrlProperty =
        AvaloniaProperty.Register<WebView2Control, string>(nameof(Url), "");

    /// <summary>要加载的页面地址(本应用场景为极验本地服务页 http://127.0.0.1:port/verify)。</summary>
    public string Url
    {
        get => GetValue(UrlProperty);
        set => SetValue(UrlProperty, value);
    }

    /// <summary>内置 WebView 不可用(包装器加载失败/浏览器子进程被拦截):通知宿主窗口回退外部浏览器。</summary>
    public event Action? CreationFailed;

    /// <summary>页面加载完成(成功或失败):宿主可隐藏加载遮罩。</summary>
    public event Action? PageLoadCompleted;

    private Delegate? _navigationCompletedHandler; // 保活官方 NavigationCompleted 事件委托

    /// <summary>仅 Windows 支持;且要求系统装有 WebView2 Runtime(版本探测成功)。</summary>
    public static bool IsSupported { get; } =
        OperatingSystem.IsWindows() && Loader.IsRuntimeAvailable();

    private nint _hwnd;
    private string _pendingUrl = "";
    private object? _managedEnv;   // 官方 CoreWebView2Environment(保活原生环境)
    private object? _managedCtrl;  // 官方 CoreWebView2Controller
    private bool _ctrlStarted;
    private bool _boundsFailLogged;
    private (int L, int T, int R, int B) _lastAppliedBounds;
    private readonly System.Collections.Concurrent.ConcurrentQueue<Action> _nativeActions = new();
    private GCHandle _selfHandle; // 保活自身(WndProc/回调链路引用)

    /// <summary>hwnd → 控件实例,供 WndProc(WM_SIZE/WM_APP)路由回实例。</summary>
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<nint, WebView2Control> s_instances = new();

    /// <summary>自定义消息:在拥有 HWND 的原生线程上执行 _nativeActions 里排队的动作。</summary>
    private const uint WmAppRunAction = 0x8000 + 1; // WM_APP+1

    public WebView2Control()
    {
        // UI 线程回调里把 Url 缓存进普通字段,供原生线程上的导航读取
        UrlProperty.Changed.AddClassHandler<WebView2Control>((o, e) => o._pendingUrl = (string)(e.NewValue ?? ""));
        // 布局尺寸变化:转投到原生线程同步给 Controller(托管 Bounds 也必须在原生线程设置)
        LayoutUpdated += (_, _) =>
        {
            if (_managedCtrl != null && _hwnd != nint.Zero)
            {
                PostToNativeThread(UpdateBounds);
            }
        };
    }

    /// <summary>把动作调度到拥有 WebView2/HWND 的原生线程执行(WebView2 COM 调用必须在该线程)。</summary>
    private void PostToNativeThread(Action action)
    {
        _nativeActions.Enqueue(action);
        var posted = Win32.PostActionMessage(_hwnd, WmAppRunAction);
        if (!posted)
        {
            Log?.LogWarning("PostMessage 失败(队列动作待排空 {Count} 个)", _nativeActions.Count);
        }
    }

    /// <summary>窗口关闭前调用:在原生线程上关闭 WebView2,赶在 HWND 销毁之前。</summary>
    public void CloseWebView() => PostToNativeThread(Teardown);

    private static nint OuterWndProc(nint hwnd, uint msg, nint wParam, nint lParam)
    {
        // 锚点窗口(预热用):只承载动作队列
        if (msg == WmAppRunAction && hwnd == s_anchorHwnd)
        {
            while (s_anchorActions.TryDequeue(out var action))
            {
                try
                {
                    action();
                }
                catch (Exception ex)
                {
                    Log?.LogError("锚点动作执行失败: {Message}", ex.Message);
                }
            }
            return nint.Zero;
        }
        // 仅处理两类自定义/布局消息,其余全部走默认窗口过程
        if (!s_instances.TryGetValue(hwnd, out var self))
        {
            return Win32.DefWindowProcW(hwnd, msg, wParam, lParam);
        }
        if (msg == WmAppRunAction)
        {
            while (self._nativeActions.TryDequeue(out var action))
            {
                try
                {
                    action();
                }
                catch (Exception ex)
                {
                    Log?.LogError("原生线程动作执行失败: {Message}", ex.Message);
                }
            }
            return nint.Zero;
        }
        if (msg == 0x0005) // WM_SIZE:Avalonia 在原生线程上 SetWindowPos 时触发,此处设置 Bounds 线程正确
        {
            self.UpdateBounds();
        }
        return Win32.DefWindowProcW(hwnd, msg, wParam, lParam);
    }

    protected override IPlatformHandle CreateNativeControlCore(IPlatformHandle parent)
    {
        if (!OperatingSystem.IsWindows())
        {
            return base.CreateNativeControlCore(parent);
        }

        _hwnd = Win32.CreateChildWindow(parent.Handle);
        if (_hwnd == nint.Zero)
        {
            return base.CreateNativeControlCore(parent);
        }
        s_instances[_hwnd] = this;

        // 保活:WndProc 与回调链路引用控件
        _selfHandle = GCHandle.Alloc(this);

        Log?.LogInformation("CreateEnvironment 发起 (thread={ThreadId})", Environment.CurrentManagedThreadId);
        StartEnvironmentCreation();
        return new HwndHandle(_hwnd);
    }

    /// <summary>
    /// 进程级缓存的官方环境:创建一次常驻复用(浏览器进程与磁盘缓存的极验 JS 均保持热态,
    /// 后续验证窗口秒开,对齐 Haiyu 的常驻模式)。环境绑定创建线程套间,所有使用须在平台线程。
    /// </summary>
    private static object? s_sharedEnv;
    private static TaskCompletionSource<object?>? s_envTcs;

    // —— 浏览器进程锚点:隐藏窗口 + 常驻 Controller ——
    // 没有任何存活的 Controller 时 WebView2 浏览器进程会自行退出,下次创建 Controller 就是冷启动(数秒)。
    // 锚点把浏览器进程钉住:验证窗口关闭后,下次 Controller 创建仅 ~70ms(实测)。
    private static nint s_anchorHwnd;
    private static object? s_anchorCtrl; // 常驻 Controller(进程生命周期)
    private static readonly System.Collections.Concurrent.ConcurrentQueue<Action> s_anchorActions = new();

    /// <summary>
    /// 反射加载官方托管包装器并创建环境:CreateAsync(null, userDataFolder, null)。
    /// 完成回调可能在墙尾线程,后续 Controller 创建经 WM_APP 队列转回原生线程。
    /// </summary>
    private void StartEnvironmentCreation()
    {
        // NativeAOT 不支持 Assembly.LoadFrom 加载外置托管程序集,直接走 CreationFailed → 系统浏览器回退。
        // 判据必须用运行时信号(AOT 下 Assembly.Location 为空):csproj 无条件 PublishAot=true
        // 会让 RuntimeFeature.IsDynamicCodeSupported 被 Roslyn 编译期折叠为 false,JIT 构建也误判
        if (string.IsNullOrEmpty(typeof(WebView2Control).Assembly.Location))
        {
            Log?.LogInformation("内置 WebView2 不可用(NativeAOT 不支持运行时加载包装器),回退系统浏览器");
            PostToNativeThread(OnCreationFailed);
            return;
        }
        // 快路径:环境已就绪(预热或前次验证创建),直接复用
        if (s_sharedEnv is not null)
        {
            Log?.LogInformation("复用常驻环境(热)");
            _managedEnv = s_sharedEnv;
            PostToNativeThread(TryCreateControllerOnPlatformThread);
            return;
        }
        // 已有创建任务在跑(如启动预热):等它完成后复用
        if (s_envTcs is not null)
        {
            s_envTcs.Task.ContinueWith(t =>
            {
                if (!t.IsCompletedSuccessfully)
                {
                    PostToNativeThread(OnCreationFailed);
                    return;
                }
                _managedEnv = t.Result;
                PostToNativeThread(TryCreateControllerOnPlatformThread);
            }, TaskScheduler.Default);
            return;
        }
        // 首次:发起创建(平台线程),完成后回到平台线程继续。
        // 用返回值而非回读 s_envTcs:失败路径会立即把静态槽置 null(允许重试),回读会 NRE。
        StartEnvironmentCreationCore().Task.ContinueWith(t =>
        {
            if (!t.IsCompletedSuccessfully)
            {
                PostToNativeThread(OnCreationFailed);
                return;
            }
            _managedEnv = t.Result;
            PostToNativeThread(TryCreateControllerOnPlatformThread);
        }, TaskScheduler.Default);
    }

    // ─── 反射区(至 SetMember 尾部)───────────────────────────────────────────────
    // 官方 WebView2 托管包装器 Microsoft.Web.WebView2.Core.dll 以随包外置 IL 形式
    // 经 Assembly.LoadFrom + 晚绑定调用:目标成员不在本程序集的裁剪/AOT 编译边界内,
    // 分析器无法静态验证,IL2026/IL2072/IL2075 为设计固有噪声。
    // 安全性:任何反射失败都走 CreationFailed → 系统浏览器回退,不存在静默损坏路径。
#pragma warning disable IL2026, IL2072, IL2075

    // 反射区各方法 UnconditionalSuppressMessage 共用理由(持久进 IL,ILC 裁剪阶段生效)
    private const string ReflJustification =
        "官方 WebView2 托管包装器为随包外置 IL 程序集(Assembly.LoadFrom 晚绑定)," +
        "目标成员不在本程序集裁剪边界内;反射失败有 CreationFailed→系统浏览器回退,无静默损坏路径。";

    /// <summary>
    /// 预热 WebView2 环境与浏览器进程(须在平台线程调用,App 初始化即在该线程)。
    /// 无库街区账号时由 App 启动调用,使后续应用内验证秒开;已有账号则不调用,避免每次启动空耗资源。
    /// 已预热/已就绪时为空操作。预热包含:环境创建 + 隐藏锚点窗口 + 常驻锚点 Controller(保活浏览器进程)。
    /// </summary>
    [UnconditionalSuppressMessage("ReflectionAnalysis", "IL2075", Justification = ReflJustification)]
    public static void PrewarmEnvironment()
    {
        if (s_sharedEnv is not null || s_envTcs is not null)
        {
            Log?.LogInformation("WebView2 预热跳过(已就绪或进行中)");
            return;
        }
        // NativeAOT 下包装器不可加载(判据同 StartEnvironmentCreation:Assembly.Location 运行时信号,
        // 勿用 IsDynamicCodeSupported——PublishAot=true 使其在 JIT 构建也被折叠为 false)
        if (string.IsNullOrEmpty(typeof(WebView2Control).Assembly.Location))
        {
            Log?.LogInformation("WebView2 预热跳过(NativeAOT 不支持运行时加载托管包装器)");
            return;
        }
        Log?.LogInformation("WebView2 环境预热开始");
        // 锚点窗口:隐藏的顶级窗口,承载常驻 Controller(须在本线程创建,消息经本线程泵投递)
        s_anchorHwnd = Win32.CreateHiddenAnchorWindow();
        // 用返回值挂续体:失败路径会把 s_envTcs 静态槽置 null,回读会 NRE(2026-09 崩溃修复)
        StartEnvironmentCreationCore().Task.ContinueWith(t =>
        {
            if (!t.IsCompletedSuccessfully)
            {
                Log?.LogError("预热失败(浏览器进程未常驻): {Ex}", t.Exception?.GetBaseException().Message);
                s_envTcs = null; // 允许下次重试
                // 环境未就绪,锚点窗口不能白留;窗口属主线程创建,销毁必须 Post 回属主线程泵
                var anchor = s_anchorHwnd;
                if (anchor != nint.Zero)
                {
                    PostToAnchorThread(() => Win32.DestroyWindow(anchor));
                    s_anchorHwnd = nint.Zero;
                }
                return;
            }
            Log?.LogInformation("环境就绪,创建常驻锚点 Controller");
            PostToAnchorThread(() =>
            {
                try
                {
                    var envType = s_sharedEnv!.GetType();
                    var ctrlTask = (Task)envType.InvokeMember("CreateCoreWebView2ControllerAsync",
                        BindingFlags.Public | BindingFlags.Instance | BindingFlags.InvokeMethod,
                        null, s_sharedEnv, new object?[] { s_anchorHwnd })!;
                    ctrlTask.ContinueWith(ct =>
                    {
                        if (ct.IsCompletedSuccessfully)
                        {
                            s_anchorCtrl = ct.GetType().GetProperty("Result")!.GetValue(ct);
                            Log?.LogInformation("浏览器进程已常驻(锚点 Controller 就绪),后续验证窗口秒开");
                            PostToAnchorThread(PrewarmAnchorNavigation);
                        }
                        else
                        {
                            Log?.LogError("锚点 Controller 创建失败: {Ex}", ct.Exception?.GetBaseException().Message);
                            s_envTcs = null;
                        }
                    }, TaskScheduler.Default);
                }
                catch (Exception ex)
                {
                    Log?.LogError("锚点 Controller 创建发起失败: {Ex}", ex.Message);
                    s_envTcs = null;
                }
            });
        }, TaskScheduler.Default);
    }

    /// <summary>锚点 Controller 预载真实极验页:让浏览器渲染器初始化与极验 CDN 资源
    /// (static.geetest.com)在启动阶段就完成加载并落入磁盘缓存,首次验证即秒开。</summary>
    [UnconditionalSuppressMessage("ReflectionAnalysis", "IL2075", Justification = ReflJustification)]
    private static void PrewarmAnchorNavigation()
    {
        try
        {
            var ctrl = s_anchorCtrl;
            if (ctrl is null)
            {
                return;
            }
            var ctrlType = ctrl.GetType();
            var webview = ctrlType.GetProperty("CoreWebView2")!.GetValue(ctrl);
            if (webview is null)
            {
                return;
            }
            var navEvent = webview.GetType().GetEvent("NavigationCompleted")!;
            var argsType = navEvent.EventHandlerType!.GetMethod("Invoke")!.GetParameters()[1].ParameterType;
            var maker = typeof(WebView2Control).GetMethod(nameof(MakeEventHandler), BindingFlags.NonPublic | BindingFlags.Static)!
                .MakeGenericMethod(argsType);
            if (maker.Invoke(null, new object[]
                {
                    (Action<object?>)(e =>
                    {
                        var ok = e?.GetType().GetProperty("IsSuccess")?.GetValue(e);
                        Log?.LogInformation("锚点预载完成 IsSuccess={S}", ok);
                    }),
                }) is Delegate handler)
            {
                navEvent.AddEventHandler(webview, handler);
            }
            var htmlPath = Path.Combine(AppContext.BaseDirectory, "Assets", "geetest.html");
            webview.GetType().GetMethod("Navigate", new[] { typeof(string) })!
                .Invoke(webview, new object?[] { $"file:///{htmlPath.Replace('\\', '/')}" });
            Log?.LogInformation("锚点已预载极验页(资源缓存预热)");
        }
        catch (Exception ex)
        {
            Log?.LogError("锚点预载失败(忽略): {Ex}", ex.Message);
        }
    }

    /// <summary>把动作调度到锚点窗口线程(即平台线程)执行。</summary>
    private static void PostToAnchorThread(Action action)
    {
        s_anchorActions.Enqueue(action);
        Win32.PostActionMessage(s_anchorHwnd, WmAppRunAction);
    }

    /// <summary>发起环境创建(平台线程调用,完成回调经该线程消息循环投递)。
    /// 返回本次创建的 tcs(恒非空;失败路径会把 s_envTcs 静态槽置 null 允许重试,
    /// 调用方必须用返回值挂续体,不得回读静态槽——曾因此 NRE)。</summary>
    [UnconditionalSuppressMessage("ReflectionAnalysis", "IL2026", Justification = ReflJustification)]
    [UnconditionalSuppressMessage("ReflectionAnalysis", "IL2075", Justification = ReflJustification)]
    private static TaskCompletionSource<object?> StartEnvironmentCreationCore()
    {
        var tcs = s_envTcs = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            // WebView2 默认背景直接用主题底色(不透明):加载期为主题色空底 + 页面自带加载动画,
            // 而非透明合成(透明会带来首帧黑块、渲染卡顿与切换闪烁)
            Environment.SetEnvironmentVariable("WEBVIEW2_DEFAULT_BACKGROUND_COLOR",
                McKuro.Services.GeetVerifyService.ResolveTheme() == "dark" ? "FF10141A" : "FFFFFFFF");

            var asmPath = Path.Combine(AppContext.BaseDirectory, "Microsoft.Web.WebView2.Core.dll");
            var asm = Assembly.LoadFrom(asmPath);
            var envType = asm.GetType("Microsoft.Web.WebView2.Core.CoreWebView2Environment", throwOnError: true)!;
            var userDataFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "McKuro", "WebView2");
            Directory.CreateDirectory(userDataFolder);
            var task = (Task)envType.InvokeMember(
                "CreateAsync",
                BindingFlags.Public | BindingFlags.Static | BindingFlags.InvokeMethod,
                null, null, new object?[] { null, userDataFolder, null })!;
            task.ContinueWith(t =>
            {
                if (!t.IsCompletedSuccessfully)
                {
                    Log?.LogError("官方包装器环境创建失败: {Ex}", t.Exception?.GetBaseException().Message);
                    s_envTcs = null; // 允许下次重试
                    tcs.SetException(t.Exception!.GetBaseException());
                    return;
                }
                s_sharedEnv = t.GetType().GetProperty("Result")!.GetValue(t);
                Log?.LogInformation("官方包装器环境创建成功(进程级缓存)");
                tcs.SetResult(s_sharedEnv);
            }, TaskScheduler.Default);
        }
        catch (Exception ex)
        {
            Log?.LogError("官方包装器不可用: {Ex}", ex.Message);
            s_envTcs = null;
            tcs.SetException(ex);
        }
        return tcs;
    }

    /// <summary>在原生线程上发起 Controller 创建(官方环境对象绑定该线程套间,不得跨线程调用)。</summary>
    [UnconditionalSuppressMessage("ReflectionAnalysis", "IL2075", Justification = ReflJustification)]
    private void TryCreateControllerOnPlatformThread()
    {
        if (_managedEnv is null || _hwnd == nint.Zero || _ctrlStarted)
        {
            Log?.LogInformation("Controller 创建跳过: env={Env} hwnd={Hwnd} started={Started}", _managedEnv != null, _hwnd, _ctrlStarted);
            return;
        }
        _ctrlStarted = true;
        try
        {
            var envType = _managedEnv.GetType();
            Log?.LogInformation("Controller 创建发起 (thread={ThreadId} apartment={Apt})", Environment.CurrentManagedThreadId, Thread.CurrentThread.GetApartmentState());
            var ctrlTask = (Task)envType.InvokeMember("CreateCoreWebView2ControllerAsync",
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.InvokeMethod,
                null, _managedEnv, new object?[] { _hwnd })!;
            Log?.LogInformation("Controller 异步任务已返回");
            ctrlTask.ContinueWith(ct =>
            {
                if (!ct.IsCompletedSuccessfully)
                {
                    Log?.LogError("Controller 创建失败: {Ex}", ct.Exception?.GetBaseException().Message);
                    PostToNativeThread(OnCreationFailed);
                    return;
                }
                _managedCtrl = ct.GetType().GetProperty("Result")!.GetValue(ct);
                Log?.LogInformation("Controller 创建成功");
                PostToNativeThread(UseControllerOnPlatformThread);
            }, TaskScheduler.Default);
        }
        catch (Exception ex)
        {
            Log?.LogError("Controller 创建发起失败: {Ex}", ex.Message);
            OnCreationFailed();
        }
    }

    /// <summary>在原生线程上使用 Controller:Bounds 覆盖客户区、可见、导航;随后启动看门狗。</summary>
    [UnconditionalSuppressMessage("ReflectionAnalysis", "IL2075", Justification = ReflJustification)]
    private void UseControllerOnPlatformThread()
    {
        if (_managedCtrl is null || _hwnd == nint.Zero)
        {
            OnCreationFailed();
            return;
        }
        UpdateBounds();
        SetMember(_managedCtrl, "IsVisible", true);
        // 默认背景按主题设为不透明底色(暗色=深底/亮色=白底):加载期为主题色空底 + 页面自带加载动画。
        // 透明(alpha=0)方案实测会带来首帧黑块、渲染卡顿与切换闪烁,弃用
        try
        {
            var dark = McKuro.Services.GeetVerifyService.ResolveTheme() == "dark";
            var colorType = typeof(System.Drawing.Color);
            var themed = colorType.GetMethod("FromArgb", new[] { typeof(int), typeof(int), typeof(int), typeof(int) })!
                .Invoke(null, dark ? new object[] { 255, 16, 20, 26 } : new object[] { 255, 255, 255, 255 });
            SetMember(_managedCtrl, "DefaultBackgroundColor", themed);
        }
        catch (Exception ex)
        {
            Log?.LogWarning("设置主题背景失败(忽略): {Message}", ex.Message);
        }
        var webview = GetMember(_managedCtrl, "CoreWebView2");
        if (webview is null)
        {
            Log?.LogError("CoreWebView2 为空,回退系统浏览器");
            OnCreationFailed();
            return;
        }
        // 页面加载完成(成功或失败均触发):通知宿主淡出加载遮罩。
        // NavigationCompleted 是强类型事件 EventHandler<CoreWebView2NavigationCompletedEventArgs>,
        // 需用泛型工厂构造匹配类型的委托(直接用非泛型 EventHandler 会转换失败)
        var navEvent = webview.GetType().GetEvent("NavigationCompleted")!;
        var argsType = navEvent.EventHandlerType!.GetMethod("Invoke")!.GetParameters()[1].ParameterType;
        var maker = GetType().GetMethod(nameof(MakeEventHandler), BindingFlags.NonPublic | BindingFlags.Static)!
            .MakeGenericMethod(argsType);
        _navigationCompletedHandler = maker.Invoke(null, new object[]
        {
            (Action<object?>)(e =>
            {
                var isSuccess = e?.GetType().GetProperty("IsSuccess")?.GetValue(e);
                Log?.LogInformation("NavigationCompleted IsSuccess={S}", isSuccess);
                Services.GeetVerifyService.TimingLog($"NavigationCompleted IsSuccess={isSuccess}");
                PageLoadCompleted?.Invoke();
            }),
        }) as Delegate;
        if (_navigationCompletedHandler is { } completedHandler)
        {
            navEvent.AddEventHandler(webview, completedHandler);
        }
        if (!string.IsNullOrWhiteSpace(_pendingUrl))
        {
            webview.GetType().GetMethod("Navigate", new[] { typeof(string) })!
                .Invoke(webview, new object?[] { _pendingUrl });
            Log?.LogInformation("Navigate({Url}) 已提交", _pendingUrl);
            Services.GeetVerifyService.TimingLog("Navigate 已提交");
        }
        // 看门狗:8s 后仍无 Chromium 渲染子窗口 = 浏览器子进程被安全软件静默拦截,
        // 触发 CreationFailed 让调用方回退系统浏览器,而不是让用户对黑屏等到超时。
        // 必须在墙池线程等待:平台线程是 WebView2 宿主 IPC/消息处理线程,Thread.Sleep 会
        // 阻塞页面加载(实测脚本请求停滞整整 8000ms)
        _ = Task.Run(async () =>
        {
            await Task.Delay(8000);
            if (_managedCtrl is null || _hwnd == nint.Zero)
            {
                return;
            }
            if (Win32.HasChildWindows(_hwnd))
            {
                Log?.LogInformation("看门狗通过:渲染子窗口已出现");
                return;
            }
            Log?.LogError("WebView2 看门狗:8s 后宿主仍无渲染子窗口,浏览器子进程疑似被安全软件拦截,回退系统浏览器");
            PostToNativeThread(OnCreationFailed);
        });
    }

    /// <summary>构造强类型事件委托的泛型工厂(反射挂接官方事件用)。</summary>
    private static Delegate MakeEventHandler<TArgs>(Action<object?> callback) where TArgs : class
    {
        return new EventHandler<TArgs>((s, e) => callback(e));
    }

    /// <summary>同步 WebView2 到容器 HWND 的物理客户区。必须在原生线程调用(WM_SIZE/WM_APP 驱动)。</summary>
    [UnconditionalSuppressMessage("ReflectionAnalysis", "IL2072", Justification = ReflJustification)]
    [UnconditionalSuppressMessage("ReflectionAnalysis", "IL2075", Justification = ReflJustification)]
    private void UpdateBounds()
    {
        if (_managedCtrl is null || _hwnd == nint.Zero || !Win32.TryGetClientRect(_hwnd, out var rc))
        {
            return;
        }
        var key = (rc.Left, rc.Top, rc.Right, rc.Bottom);
        if (key == _lastAppliedBounds)
        {
            return;
        }
        try
        {
            var ctrlType = _managedCtrl.GetType();
            var boundsType = ctrlType.GetProperty("Bounds")!.PropertyType;
            // 官方包装器 Bounds 类型为 System.Drawing.Rectangle(x, y, width, height)
            var bounds = Activator.CreateInstance(boundsType, rc.Left, rc.Top, rc.Right - rc.Left, rc.Bottom - rc.Top);
            ctrlType.GetProperty("Bounds")!.SetValue(_managedCtrl, bounds);
            _lastAppliedBounds = key;
            Log?.LogInformation("put_Bounds ({L},{T})-({R},{B})", rc.Left, rc.Top, rc.Right, rc.Bottom);
        }
        catch (Exception ex)
        {
            if (!_boundsFailLogged)
            {
                _boundsFailLogged = true;
                Log?.LogWarning("设置 Bounds 失败: {Message}", ex.Message);
            }
        }
    }

    [UnconditionalSuppressMessage("ReflectionAnalysis", "IL2075", Justification = ReflJustification)]
    private static object? GetMember(object target, string name)
    {
        var type = target.GetType();
        return type.GetProperty(name)?.GetValue(target) ?? type.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)?.GetValue(target);
    }

    [UnconditionalSuppressMessage("ReflectionAnalysis", "IL2075", Justification = ReflJustification)]
    private static void SetMember(object target, string name, object? value)
    {
        var type = target.GetType();
        var prop = type.GetProperty(name);
        if (prop is not null)
        {
            prop.SetValue(target, value);
        }
        else
        {
            type.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)?.SetValue(target, value);
        }
    }

#pragma warning restore IL2026, IL2072, IL2075
    // ─── 反射区结束 ──────────────────────────────────────────────────────────────

    /// <summary>失败收尾:清理托管引用并触发回退事件。</summary>
    private void OnCreationFailed()
    {
        // 必须在 Teardown 前捕获存活状态:Teardown 会把 _hwnd 置零,若事后再判"窗口已关闭"
        // 恒为真,CreationFailed 回退事件永远不触发(2026-09 AOT 冒烟暴露;此前被 8s 看门狗掩盖)
        var windowWasAlive = _hwnd != nint.Zero;
        Teardown();
        // 窗口已关闭(用户取消/窗口销毁后,在途异步创建才完成):不再触发回退,
        // 否则用户明明取消了验证,系统浏览器还会弹出验证页
        if (!windowWasAlive)
        {
            Log?.LogInformation("窗口已关闭,跳过 CreationFailed 回退");
            return;
        }
        CreationFailed?.Invoke();
    }

    /// <summary>
    /// 释放资源。托管对象调用 Close 后交由官方包装器管理生命周期;
    /// 绝不直接对原生 COM 对象调 Release(浏览器子进程死亡时对象已被 WebView2 自行销毁,
    /// 悬空指针调用是 AccessViolation 且不可捕获)。
    /// </summary>
    /// <summary>
    /// 释放资源。注意两点:
    /// 1. 不调用 Controller.Close——加载中调用会同步等待进行中的异步创建,而其完成依赖本线程
    ///    消息循环,形成死锁(窗口未响应);环境进程级常驻,弃用的 Controller 交由 WebView2
    ///    随宿主 HWND 销毁自行清理即可。
    /// 2. 绝不直接对原生 COM 对象调 Release——浏览器子进程死亡时对象已被自行销毁,悬空指针
    ///    调用是 AccessViolation 且不可捕获。
    /// </summary>
    private void Teardown()
    {
        Log?.LogInformation("Teardown (thread={ThreadId})", Environment.CurrentManagedThreadId);
        _managedCtrl = null;
        var hwnd = Interlocked.Exchange(ref _hwnd, nint.Zero);
        if (hwnd != nint.Zero)
        {
            s_instances.TryRemove(hwnd, out _);
            Win32.DestroyWindow(hwnd);
        }
        try
        {
            if (_selfHandle.IsAllocated)
            {
                _selfHandle.Free();
            }
        }
        catch (Exception ex)
        {
            Log?.LogError("Teardown Free GCHandle 失败(已忽略): {Message}", ex.Message);
        }
    }

    protected override void DestroyNativeControlCore(IPlatformHandle control)
    {
        Teardown();
        base.DestroyNativeControlCore(control);
    }

    private sealed class HwndHandle(nint handle) : IPlatformHandle
    {
        public nint Handle { get; } = handle;
        public string HandleDescriptor => "HWND";
    }

    /// <summary>WebView2Loader 导入(仅运行时探测;环境/Controller 创建走官方托管包装器)。</summary>
    private static class Loader
    {
        private const int S_OK = 0;

        [DllImport("WebView2Loader", CharSet = CharSet.Unicode, ExactSpelling = true)]
        private static extern int GetAvailableCoreWebView2BrowserVersionString(string? browserExecutableFolder, out nint version);

        /// <summary>探测 WebView2 Evergreen Runtime 是否可用(未装 Runtime 的系统返回 false)。</summary>
        public static bool IsRuntimeAvailable()
        {
            if (!OperatingSystem.IsWindows())
            {
                return false;
            }
            try
            {
                return GetAvailableCoreWebView2BrowserVersionString(null, out _) == S_OK;
            }
            catch (Exception)
            {
                // WebView2Loader.dll 不在输出目录(非 Windows 构建产物)等
                return false;
            }
        }
    }

    /// <summary>Win32 子窗口(WebView2 Controller 的父 HWND)与消息队列。</summary>
    private static class Win32
    {
        private const uint WS_CHILD = 0x40000000;
        private const uint WS_VISIBLE = 0x10000000;
        private const uint WS_CLIPCHILDREN = 0x02000000;
        private const uint WS_CLIPSIBLINGS = 0x04000000;

        private static bool _classRegistered;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct WndClassW
        {
            public uint Style;
            public nint LpfnWndProc;
            public int CbClsExtra;
            public int CbWndExtra;
            public nint HInstance;
            public nint HIcon;
            public nint HCursor;
            public nint HbrBackground;
            [MarshalAs(UnmanagedType.LPWStr)] public string? LpszMenuName;
            [MarshalAs(UnmanagedType.LPWStr)] public string LpszClassName;
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern ushort RegisterClassW(ref WndClassW lpWndClass);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern nint CreateWindowExW(
            uint dwExStyle, string lpClassName, string? lpWindowName, uint dwStyle,
            int x, int y, int width, int height,
            nint hWndParent, nint hMenu, nint hInstance, nint lpParam);

        // user32 导出名是 DestroyWindow,本类外层包装方法已占用同名,故 extern 名带 Native 后缀
        [DllImport("user32.dll", EntryPoint = "DestroyWindow", SetLastError = true)]
        private static extern bool DestroyWindowNative(nint hwnd);

        [DllImport("user32.dll")]
        internal static extern bool IsWindow(nint hwnd);

        [DllImport("user32.dll")]
        private static extern bool GetClientRect(nint hwnd, out NativeRect lpRect);

        [DllImport("kernel32.dll")]
        private static extern nint GetModuleHandleW(string? name);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate nint WindowProcDef(nint hwnd, uint msg, nint wParam, nint lParam);

        [DllImport("user32.dll")]
        internal static extern nint DefWindowProcW(nint hwnd, uint msg, nint wParam, nint lParam);

        private const string ClassName = "McKuroWebView2Host";

        private static readonly WindowProcDef WindowProc = OuterWndProc;

        public static nint CreateChildWindow(nint parentHwnd)
        {
            try
            {
                EnsureClass();
                return CreateWindowExW(
                    0, ClassName, null,
                    WS_CHILD | WS_VISIBLE | WS_CLIPCHILDREN | WS_CLIPSIBLINGS,
                    0, 0, 1, 1,
                    parentHwnd, nint.Zero, GetModuleHandleW(null), nint.Zero);
            }
            catch (Exception)
            {
                return nint.Zero;
            }
        }

        private static void EnsureClass()
        {
            if (_classRegistered)
            {
                return;
            }
            // 宿主子窗口类背景刷按应用主题取色(WebView2 默认背景透明时透出的是本窗口的类背景,
            // 无刷/色不符会在加载期呈黑块或白块)。类注册进程内仅一次,以首次创建时的主题为准。
            var dark = McKuro.Services.GeetVerifyService.ResolveTheme() == "dark";
            // COLORREF 为 0x00BBGGRR
            var hbr = CreateSolidBrush(dark ? 0x001A1410u : 0x00FCFAF8u);
            var wc = new WndClassW
            {
                LpfnWndProc = Marshal.GetFunctionPointerForDelegate(WindowProc),
                HInstance = GetModuleHandleW(null),
                HbrBackground = hbr,
                LpszClassName = ClassName,
            };
            RegisterClassW(ref wc);
            _classRegistered = true;
        }

        [DllImport("gdi32.dll")]
        private static extern nint CreateSolidBrush(uint color);

        private const uint WS_POPUP = 0x80000000;

        /// <summary>创建隐藏的锚点窗口(预热用,承载常驻 Controller 保活浏览器进程)。</summary>
        public static nint CreateHiddenAnchorWindow()
        {
            EnsureClass();
            // 不带 WS_VISIBLE:窗口不可见;移到屏幕外兜底。控制器在隐藏父窗上创建即为暂停渲染的常驻模式
            return CreateWindowExW(
                0, ClassName, null,
                WS_POPUP,
                -32000, -32000, 1, 1,
                nint.Zero, nint.Zero, GetModuleHandleW(null), nint.Zero);
        }

        private static nint DefWndProc(nint hwnd, uint msg, nint wParam, nint lParam) =>
            DefWindowProcW(hwnd, msg, wParam, lParam);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool PostMessageW(nint hwnd, uint msg, nint wParam, nint lParam);

        /// <summary>向宿主子窗口(拥有 WebView2 的原生线程)投递自定义消息以执行排队动作。</summary>
        internal static bool PostActionMessage(nint hwnd, uint msg) =>
            hwnd != nint.Zero && PostMessageW(hwnd, msg, nint.Zero, nint.Zero);

        [StructLayout(LayoutKind.Sequential)]
        internal struct NativeRect
        {
            public int Left, Top, Right, Bottom;
        }

        public static bool TryGetClientRect(nint hwnd, out NativeRect rect)
        {
            if (hwnd != nint.Zero)
            {
                return GetClientRect(hwnd, out rect);
            }
            rect = default;
            return false;
        }

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate bool EnumChildProc(nint hwnd, nint lParam);

        [DllImport("user32.dll")]
        private static extern bool EnumChildWindows(nint parent, EnumChildProc proc, nint lParam);

        /// <summary>宿主 HWND 下是否已有子窗口(WebView2 渲染子窗口出现 = 渲染管线可用)。</summary>
        internal static bool HasChildWindows(nint parent)
        {
            var found = false;
            EnumChildWindows(parent, (h, _) => { found = true; return false; }, nint.Zero);
            return found;
        }

        public static void DestroyWindow(nint hwnd)
        {
            if (hwnd != nint.Zero)
            {
                try
                {
                    DestroyWindowNative(hwnd);
                }
                catch (Exception ex)
                {
                    // 清理失败不能带崩调用方(失败回退路径必须始终可走)
                    Log?.LogError("DestroyWindow 失败 hwnd=0x{Hwnd:X}: {Message}", hwnd, ex.Message);
                }
            }
        }
    }
}
