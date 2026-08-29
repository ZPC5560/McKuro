using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;

namespace McKuro.Controls;

/// <summary>
/// 应用内嵌 WebView 控件(Windows):用系统 WebView2(Evergreen Runtime)显示极验滑块页,
/// 替代跳转系统浏览器。通过 <see cref="NativeControlHost"/> 把 WebView2 的 HWND 挂进 Avalonia 视觉树。
/// <para>
/// AOT 说明:官方 Microsoft.Web.WebView2 托管包是 CsWinRT 互操作,与 Native AOT 不兼容
/// (csproj 已 ExcludeAssets=all,只部署原生 WebView2Loader.dll)。COM 接口在此手写最小互操作:
/// 调用时按 vtable 槽位取函数指针(槽位顺序取自 SDK WebView2.h,COM 规则保证只追加不重排),
/// 回调 COM 对象手动构造 vtable,无 CsWinRT/反射。仅需 Environment(建 Controller)/Controller
/// (可见性、Bounds、Close、取 WebView2)/WebView2(Navigate)三类接口。
/// </para>
/// <para>
/// 验证结果不走 JS 桥:与 macOS 相同,极验页完成后由 GeetVerifyService 的本地 HTTP 回调(/cb)接收,
/// 本控件只负责显示。运行时缺失(Win10 早期/LTSC 精简系统)时 <see cref="IsSupported"/> 为 false,
/// 调用方回退系统浏览器。
/// </para>
/// </summary>
public sealed class WebView2Control : NativeControlHost
{
    public static readonly StyledProperty<string> UrlProperty =
        AvaloniaProperty.Register<WebView2Control, string>(nameof(Url), "");

    /// <summary>要加载的页面地址(本应用场景为极验本地服务页 http://127.0.0.1:port/verify)。</summary>
    public string Url
    {
        get => GetValue(UrlProperty);
        set => SetValue(UrlProperty, value);
    }

    /// <summary>WebView2 环境创建失败(运行时缺失等):通知宿主窗口回退外部浏览器。</summary>
    public event Action? CreationFailed;

    /// <summary>仅 Windows 支持;且要求系统装有 WebView2 Runtime(版本探测成功)。</summary>
    public static bool IsSupported { get; } =
        OperatingSystem.IsWindows() && Loader.IsRuntimeAvailable();

    private nint _hwnd;
    private nint _environment;   // ICoreWebView2Environment*(持有 1 引用)
    private nint _controller;    // ICoreWebView2Controller*(持有 1 引用)
    private nint _webview;       // ICoreWebView2*(持有 1 引用)
    private bool _navigationDone;
    private GCHandle _selfHandle; // 保活自身(布局/属性事件与回调链路引用)

    public WebView2Control()
    {
        UrlProperty.Changed.AddClassHandler<WebView2Control>((o, _) => o.NavigateIfReady());
        LayoutUpdated += (_, _) => UpdateBounds();
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

        // 保活:回调为原生->托管 thunk,控件先于回调被 GC 会导致崩溃
        _selfHandle = GCHandle.Alloc(this);

        // WebView2 创建链为异步:env → controller → navigate;完成回调经 UI 线程消息循环返回
        Loader.CreateEnvironment(OnEnvironmentCreated, OnCreationFailed);
        return new HwndHandle(_hwnd);
    }

    protected override void DestroyNativeControlCore(IPlatformHandle control)
    {
        Teardown();
        base.DestroyNativeControlCore(control);
    }

    private void OnEnvironmentCreated(nint environment)
    {
        if (_hwnd == nint.Zero)
        {
            Com.ReleaseComObject(environment);
            return;
        }
        _environment = environment;
        Com.CreateController(environment, _hwnd, OnControllerCreated, OnCreationFailed);
    }

    private void OnControllerCreated(nint controller)
    {
        if (_hwnd == nint.Zero)
        {
            Com.ReleaseComObject(controller);
            return;
        }
        _controller = controller;
        Com.PutIsVisible(controller, true);
        UpdateBounds();
        if (Com.TryGetWebView2(controller, out var webview))
        {
            _webview = webview;
            NavigateIfReady();
        }
        else
        {
            OnCreationFailed();
        }
    }

    private void OnCreationFailed()
    {
        Teardown();
        CreationFailed?.Invoke();
    }

    private void NavigateIfReady()
    {
        if (_webview != nint.Zero && !_navigationDone && !string.IsNullOrWhiteSpace(Url))
        {
            _navigationDone = true;
            Com.Navigate(_webview, Url);
        }
    }

    /// <summary>同步 WebView2 到容器 HWND 的物理客户区(布局/缩放变化时由 LayoutUpdated 驱动)。</summary>
    private void UpdateBounds()
    {
        if (_controller != nint.Zero && _hwnd != nint.Zero && Win32.TryGetClientRect(_hwnd, out var rc))
        {
            Com.PutBounds(_controller, rc);
        }
    }

    private void Teardown()
    {
        if (_webview != nint.Zero)
        {
            Com.ReleaseComObject(_webview);
            _webview = nint.Zero;
        }
        if (_controller != nint.Zero)
        {
            Com.CloseController(_controller);
            Com.ReleaseComObject(_controller);
            _controller = nint.Zero;
        }
        if (_environment != nint.Zero)
        {
            Com.ReleaseComObject(_environment);
            _environment = nint.Zero;
        }
        if (_hwnd != nint.Zero)
        {
            Win32.DestroyWindow(_hwnd);
            _hwnd = nint.Zero;
        }
        if (_selfHandle.IsAllocated)
        {
            _selfHandle.Free();
        }
    }

    private sealed class HwndHandle(nint handle) : IPlatformHandle
    {
        public nint Handle { get; } = handle;
        public string HandleDescriptor => "HWND";
    }

    // ============ COM vtable 互操作(槽位顺序取自 SDK WebView2.h;COM 规则:接口只追加方法,槽位稳定) ============

    /// <summary>ICoreWebView2Environment / Controller / WebView2 方法调用(按对象 vtable 槽位解析)。</summary>
    private static class Com
    {
        // ICoreWebView2Environment 槽位 3:CreateCoreWebView2Controller(parentWindow, handler)
        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int CreateControllerProto(nint self, nint parentHwnd, nint handler);

        // ICoreWebView2Controller 槽位 4:put_IsVisible(BOOL)
        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int PutIsVisibleProto(nint self, int visible);

        // 槽位 6:put_Bounds(RECT 16 字节,x64 ABI 按指针传 → ref)
        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int PutBoundsProto(nint self, ref NativeRect bounds);

        // 槽位 24:Close
        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int CloseProto(nint self);

        // 槽位 25:get_CoreWebView2(out ICoreWebView2*)
        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int GetWebView2Proto(nint self, out nint webview);

        // ICoreWebView2 槽位 5:Navigate(LPCWSTR)
        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int NavigateProto(nint self, nint uri);

        /// <summary>取 COM 对象 vtable 第 slot 个方法并组为委托(每次调用解析,量级可忽略)。</summary>
        private static T GetSlot<T>(nint comObject, int slot) where T : Delegate
        {
            // *comObject = vtable 指针;vtbl[slot] = 方法指针
            var vtbl = Marshal.ReadIntPtr(comObject);
            var fn = Marshal.ReadIntPtr(vtbl, slot * IntPtr.Size);
            return Marshal.GetDelegateForFunctionPointer<T>(fn);
        }

        public static void CreateController(nint environment, nint parentHwnd, Action<nint> onCreated, Action onFailed)
        {
            // handler 引用计数 1(创建者),CreateCoreWebView2Controller 成功后归 WebView2 释放
            var handler = ComCallback.CreateCompleted((hr, payload) =>
            {
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    if (hr != 0 || payload == nint.Zero)
                    {
                        onFailed();
                    }
                    else
                    {
                        // payload 引用归本控件(OnControllerCreated 负责 Release)
                        onCreated(payload);
                    }
                });
                return 0;
            });
            var hr2 = GetSlot<CreateControllerProto>(environment, 3)(environment, parentHwnd, handler);
            if (hr2 != 0)
            {
                // 同步失败:handler 未被接管,手动释放
                ComCallback.ReleaseComObject(handler);
                onFailed();
            }
        }

        public static void PutIsVisible(nint controller, bool visible) =>
            GetSlot<PutIsVisibleProto>(controller, 4)(controller, visible ? 1 : 0);

        public static void PutBounds(nint controller, NativeRect rc) =>
            GetSlot<PutBoundsProto>(controller, 6)(controller, ref rc);

        public static void CloseController(nint controller) => GetSlot<CloseProto>(controller, 24)(controller);

        public static bool TryGetWebView2(nint controller, out nint webview)
        {
            var hr = GetSlot<GetWebView2Proto>(controller, 25)(controller, out webview);
            return hr == 0 && webview != nint.Zero;
        }

        public static void Navigate(nint webview, string url)
        {
            var ptr = Marshal.StringToHGlobalUni(url);
            try
            {
                GetSlot<NavigateProto>(webview, 5)(webview, ptr);
            }
            finally
            {
                Marshal.FreeHGlobal(ptr);
            }
        }

        /// <summary>对 WebView2 返回给我们的 COM 对象调用 IUnknown::Release。</summary>
        public static void ReleaseComObject(nint comObject) => ComCallback.ReleaseComObject(comObject);

        [StructLayout(LayoutKind.Sequential)]
        internal struct NativeRect
        {
            public int Left, Top, Right, Bottom;
            public NativeRect(int l, int t, int r, int b)
            {
                Left = l;
                Top = t;
                Right = r;
                Bottom = b;
            }
        }
    }

    /// <summary>
    /// 手动构造的 COM 回调对象:内存 [对象: vtbl 指针, 引用计数, Invoke 委托句柄] → [vtbl: QI/AddRef/Release/Invoke]。
    /// WebView2 对回调仅使用 IUnknown 与 Invoke,引用计数归零时释放全部非托管内存与委托句柄。
    /// </summary>
    private static class ComCallback
    {
        private sealed class CallbackState
        {
            public long RefCount = 1; // 创建者持有 1
            public Delegate Invoke = null!; // 保活 vtbl 引用的包装委托
        }

        private const int S_OK = 0;
        private const int E_NOINTERFACE = unchecked((int)0x80004002);
        private const int E_POINTER = unchecked((int)0x80004003);

        // IID_IUnknown {00000000-0000-0000-C000-000000000046}:前 8 字节为 0,后 8 字节小端 0x46000000000000C0
        private const long IidIUnknownLow = 0;
        private const long IidIUnknownHigh = unchecked((long)0x46000000000000C0);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int QueryInterfaceProto(nint self, nint riid, nint ppv);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate uint AddRefProto(nint self);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate uint ReleaseProto(nint self);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        public delegate int InvokeProto(nint self, int errorCode, nint payload);

        private static readonly QueryInterfaceProto QueryInterfaceImpl = OnQueryInterface;
        private static readonly AddRefProto AddRefImpl = OnAddRef;
        private static readonly ReleaseProto ReleaseImpl = OnRelease;

        /// <summary>创建完成回调 COM 对象(Invoke(errorCode, payload)),返回 IUnknown*(引用计数 1,归调用方)。</summary>
        public static nint CreateCompleted(Func<int, nint, int> onCompleted)
        {
            // 包装掉 self 参数,调用方只需关心 (errorCode, payload)
            InvokeProto wrapped = (self, errorCode, payload) => onCompleted(errorCode, payload);
            var vtbl = Marshal.AllocHGlobal(IntPtr.Size * 4);
            Marshal.WriteIntPtr(vtbl, 0 * IntPtr.Size, Marshal.GetFunctionPointerForDelegate(QueryInterfaceImpl));
            Marshal.WriteIntPtr(vtbl, 1 * IntPtr.Size, Marshal.GetFunctionPointerForDelegate(AddRefImpl));
            Marshal.WriteIntPtr(vtbl, 2 * IntPtr.Size, Marshal.GetFunctionPointerForDelegate(ReleaseImpl));
            Marshal.WriteIntPtr(vtbl, 3 * IntPtr.Size, Marshal.GetFunctionPointerForDelegate(wrapped));

            var stateHandle = GCHandle.Alloc(new CallbackState { Invoke = wrapped });
            var obj = Marshal.AllocHGlobal(IntPtr.Size * 2);
            Marshal.WriteIntPtr(obj, 0 * IntPtr.Size, vtbl);
            Marshal.WriteIntPtr(obj, 1 * IntPtr.Size, GCHandle.ToIntPtr(stateHandle));
            return obj;
        }

        private static CallbackState GetState(nint self) =>
            (CallbackState)GCHandle.FromIntPtr(Marshal.ReadIntPtr(self, IntPtr.Size)).Target!;

        private static int OnQueryInterface(nint self, nint riid, nint ppv)
        {
            if (ppv == nint.Zero)
            {
                return E_POINTER;
            }
            if (Marshal.ReadInt64(riid, 0) == IidIUnknownLow
                && Marshal.ReadInt64(riid, 8) == IidIUnknownHigh)
            {
                Marshal.WriteIntPtr(ppv, self);
                OnAddRef(self);
                return S_OK;
            }
            Marshal.WriteIntPtr(ppv, nint.Zero);
            return E_NOINTERFACE;
        }

        private static uint OnAddRef(nint self) =>
            (uint)Interlocked.Increment(ref GetState(self).RefCount);

        private static uint OnRelease(nint self)
        {
            var state = GetState(self);
            var remaining = (uint)Interlocked.Decrement(ref state.RefCount);
            if (remaining == 0)
            {
                var vtbl = Marshal.ReadIntPtr(self);
                GCHandle.FromIntPtr(Marshal.ReadIntPtr(self, IntPtr.Size)).Free(); // 状态句柄(含 Invoke 委托)
                Marshal.FreeHGlobal(vtbl);
                Marshal.FreeHGlobal(self);
            }
            return remaining;
        }

        /// <summary>对 WebView2 返回给我们的 COM 对象(ENV/Controller/WebView2/Handler)调用 Release。</summary>
        public static void ReleaseComObject(nint comObject)
        {
            if (comObject == nint.Zero)
            {
                return;
            }
            var vtbl = Marshal.ReadIntPtr(comObject);
            var release = Marshal.GetDelegateForFunctionPointer<ReleaseProto>(Marshal.ReadIntPtr(vtbl, 2 * IntPtr.Size));
            release(comObject);
        }
    }

    // ============ WebView2Loader.dll P/Invoke + Win32 宿主子窗口 ============

    /// <summary>WebView2Loader 导入。</summary>
    private static class Loader
    {
        private static bool _userDataFolderEnsured;
        private const int S_OK = 0;

        [DllImport("WebView2Loader", CharSet = CharSet.Unicode, ExactSpelling = true)]
        private static extern int GetAvailableCoreWebView2BrowserVersionString(string? browserExecutableFolder, out nint version);

        [DllImport("WebView2Loader", CharSet = CharSet.Unicode, ExactSpelling = true)]
        private static extern int CreateCoreWebView2EnvironmentWithOptions(
            string? browserExecutableFolder,
            string? userDataFolder,
            nint environmentOptions,   // ICoreWebView2EnvironmentOptions*:null = 默认
            nint environmentCompletedHandler);

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

        /// <summary>创建 WebView2 环境(完成回调经 UI 线程返回)。用户数据目录用环境变量指定,免实现 Options COM 对象。</summary>
        public static void CreateEnvironment(Action<nint> onCreated, Action onFailed)
        {
            try
            {
                if (!_userDataFolderEnsured)
                {
                    _userDataFolderEnsured = true;
                    // 默认用户数据目录在 exe 下(Program Files 只读),显式指到应用数据目录
                    var dir = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                        "McKuro", "WebView2");
                    Directory.CreateDirectory(dir);
                    Environment.SetEnvironmentVariable("WEBVIEW2_USER_DATA_FOLDER", dir);
                }

                var handler = ComCallback.CreateCompleted((hr, payload) =>
                {
                    Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                    {
                        if (hr != 0 || payload == nint.Zero)
                        {
                            onFailed();
                        }
                        else
                        {
                            // env 引用归本控件(OnEnvironmentCreated 负责 Release/转交)
                            onCreated(payload);
                        }
                    });
                    return 0;
                });
                var hr2 = CreateCoreWebView2EnvironmentWithOptions(null, null, nint.Zero, handler);
                if (hr2 != S_OK)
                {
                    ComCallback.ReleaseComObject(handler);
                    onFailed();
                }
            }
            catch (Exception)
            {
                onFailed();
            }
        }
    }

    /// <summary>Win32 子窗口(WebView2 Controller 的父 HWND)。</summary>
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

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool DestroyWindowNative(nint hwnd);

        [DllImport("user32.dll")]
        private static extern bool GetClientRect(nint hwnd, out Com.NativeRect lpRect);

        [DllImport("kernel32.dll")]
        private static extern nint GetModuleHandleW(string? name);

        private static readonly WindowProcDef WindowProc = DefWndProc;

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate nint WindowProcDef(nint hwnd, uint msg, nint wParam, nint lParam);

        [DllImport("user32.dll")]
        private static extern nint DefWindowProcW(nint hwnd, uint msg, nint wParam, nint lParam);

        private const string ClassName = "McKuroWebView2Host";

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
            var wc = new WndClassW
            {
                LpfnWndProc = Marshal.GetFunctionPointerForDelegate(WindowProc),
                HInstance = GetModuleHandleW(null),
                HbrBackground = nint.Zero, // 背景由 WebView2 填充,避免闪烁
                LpszClassName = ClassName,
            };
            RegisterClassW(ref wc);
            _classRegistered = true;
        }

        private static nint DefWndProc(nint hwnd, uint msg, nint wParam, nint lParam) =>
            DefWindowProcW(hwnd, msg, wParam, lParam);

        public static bool TryGetClientRect(nint hwnd, out Com.NativeRect rect)
        {
            if (hwnd != nint.Zero)
            {
                return GetClientRect(hwnd, out rect);
            }
            rect = default;
            return false;
        }

        public static void DestroyWindow(nint hwnd)
        {
            if (hwnd != nint.Zero)
            {
                DestroyWindowNative(hwnd);
            }
        }
    }
}
