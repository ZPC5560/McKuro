using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;

namespace McKuro.Controls;

/// <summary>
/// 应用内嵌 WebView 控件(macOS):用系统 WKWebView 显示极验滑块页,替代跳转系统浏览器。
/// <para>
/// 实现说明:直接 P/Invoke libobjc + dlopen WebKit.framework 手动创建 WKWebView,
/// 通过 <see cref="NativeControlHost"/> 挂进 Avalonia 视觉树。不引入 WebView 包装 NuGet
/// (官方 WebView2 不支持 Native AOT,社区包装多含反射);全程 objc_msgSend 函数指针,
/// 无反射、AOT/裁剪安全。不注册任何 ObjC 类(无需 JS 桥):极验结果仍由
/// <see cref="Services.GeetVerifyService"/> 的本地 HTTP 回调(/cb)接收,WebView 只负责显示。
/// </para>
/// <para>
/// 非 macOS 平台 <see cref="IsSupported"/> 为 false,调用方应回退系统浏览器流程。
/// </para>
/// </summary>
public sealed class WkWebViewControl : NativeControlHost
{
    public static readonly StyledProperty<string> UrlProperty =
        AvaloniaProperty.Register<WkWebViewControl, string>(nameof(Url), "");

    /// <summary>要加载的页面地址(本应用场景为极验本地服务页 http://127.0.0.1:port/verify)。</summary>
    public string Url
    {
        get => GetValue(UrlProperty);
        set => SetValue(UrlProperty, value);
    }

    /// <summary>仅 macOS(Apple Silicon)支持:Intel 下 objc_msgSend 大结构体参数需 stret 变体,未实现,回退外部浏览器。</summary>
    public static bool IsSupported { get; } =
        OperatingSystem.IsMacOS()
        && System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture == System.Runtime.InteropServices.Architecture.Arm64
        && Objc.IsWebKitAvailable;

    private IntPtr _webView;

    public WkWebViewControl()
    {
        UrlProperty.Changed.AddClassHandler<WkWebViewControl>((o, _) => o.LoadUrl());
    }

    protected override IPlatformHandle CreateNativeControlCore(IPlatformHandle parent)
    {
        if (OperatingSystem.IsMacOS())
        {
            _webView = Objc.CreateWkWebView();
            if (_webView != IntPtr.Zero)
            {
                LoadUrl();
                return new NSViewHandle(_webView);
            }
        }
        return base.CreateNativeControlCore(parent);
    }

    protected override void DestroyNativeControlCore(IPlatformHandle control)
    {
        if (_webView != IntPtr.Zero)
        {
            Objc.ReleaseObject(_webView);
            _webView = IntPtr.Zero;
        }
        base.DestroyNativeControlCore(control);
    }

    private void LoadUrl()
    {
        if (_webView != IntPtr.Zero && !string.IsNullOrWhiteSpace(Url))
        {
            Objc.LoadUrl(_webView, Url);
        }
    }

    private sealed class NSViewHandle(IntPtr handle) : IPlatformHandle
    {
        public IntPtr Handle { get; } = handle;
        public string HandleDescriptor => "NSView";
    }

    /// <summary>libobjc/WebKit 手动绑定(dlopen + objc_msgSend,无反射)。</summary>
    private static class Objc
    {
        private static readonly object InitLock = new();
        private static bool _initialized;

        private static IntPtr _libobjc;
        private static IntPtr _msgSend;
        private static IntPtr _clsWKWebView;
        private static IntPtr _clsWKConfig;
        private static IntPtr _clsNSURL;
        private static IntPtr _clsNSURLRequest;
        private static IntPtr _clsNSString;

        public static bool IsWebKitAvailable
        {
            get
            {
                EnsureInit();
                return _clsWKWebView != IntPtr.Zero;
            }
        }

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate IntPtr MsgSend2(IntPtr self, IntPtr sel);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate IntPtr MsgSendPtr(IntPtr self, IntPtr sel, IntPtr arg);

        // 关键:arm64 上 CGRect 是 4×double 的 HFA(同质浮点聚合),按平台 ABI 走 SIMD/FP 寄存器 v0-v3,
        // configuration 指针走整数寄存器。不能用 ref 结构体(会错传整数寄存器导致参数错位,
        // WKWebView _initializeWithConfiguration 内部对垃圾指针发消息 → SIGSEGV,已实测复现)。
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate IntPtr MsgSendFrameCfg(IntPtr self, IntPtr sel, double fx, double fy, double fw, double fh, IntPtr cfg);

        [DllImport("libobjc.A.dylib", SetLastError = false)]
        private static extern IntPtr sel_registerName(string name);

        [DllImport("libobjc.A.dylib", SetLastError = false)]
        private static extern IntPtr objc_getClass(string name);

        // CoreFoundation 必须用框架完整路径(裸名 dlopen 解析不到,实测 DllNotFoundException)
        [DllImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation", SetLastError = false)]
        private static extern IntPtr CFStringCreateWithCString(IntPtr alloc, string cStr, uint encoding);

        [DllImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation", SetLastError = false)]
        private static extern void CFRelease(IntPtr cf);

        private const uint KCFStringEncodingUTF8 = 0x0800_0100;

        private static void EnsureInit()
        {
            lock (InitLock)
            {
                if (_initialized)
                {
                    return;
                }
                _initialized = true;
                try
                {
                    if (!NativeLibrary.TryLoad("libobjc.A.dylib", out _libobjc))
                    {
                        return;
                    }
                    _msgSend = NativeLibrary.GetExport(_libobjc, "objc_msgSend");

                    // WebKit.framework 默认未加载,必须先 dlopen,objc_getClass 才能拿到 WKWebView
                    if (!NativeLibrary.TryLoad("/System/Library/Frameworks/WebKit.framework/WebKit", out var webKit))
                    {
                        return;
                    }
                    _clsWKWebView = objc_getClass("WKWebView");
                    _clsWKConfig = objc_getClass("WKWebViewConfiguration");
                    _clsNSURL = objc_getClass("NSURL");
                    _clsNSURLRequest = objc_getClass("NSURLRequest");
                    _clsNSString = objc_getClass("NSString");
                }
                catch (Exception)
                {
                    // 探测失败:调用方回退外部浏览器
                }
            }
        }

        private static IntPtr Sel(string name) => sel_registerName(name);

        private static T GetDelegate<T>() where T : Delegate
            => Marshal.GetDelegateForFunctionPointer<T>(_msgSend);

        // 委托懒加载:必须在 EnsureInit() 之后访问(_msgSend 才有值)
        private static MsgSend2? _send2;
        private static MsgSend2 Send2 => _send2 ??= GetDelegate<MsgSend2>();
        private static MsgSendPtr? _sendPtr;
        private static MsgSendPtr SendPtr => _sendPtr ??= GetDelegate<MsgSendPtr>();
        private static MsgSendFrameCfg? _sendFrameCfg;
        private static MsgSendFrameCfg SendFrameCfg => _sendFrameCfg ??= GetDelegate<MsgSendFrameCfg>();

        /// <summary>创建 WKWebView(默认配置、1x1 初始尺寸,布局交给 Avalonia)。</summary>
        public static IntPtr CreateWkWebView()
        {
            EnsureInit();
            try
            {
                if (_clsWKWebView == IntPtr.Zero)
                {
                    return IntPtr.Zero;
                }
                var config = Send2(Send2(_clsWKConfig, Sel("alloc")), Sel("init"));
                var webView = SendFrameCfg(Send2(_clsWKWebView, Sel("alloc")), Sel("initWithFrame:configuration:"), 0, 0, 1, 1, config);
                ReleaseObject(config);
                return webView;
            }
            catch (Exception)
            {
                return IntPtr.Zero;
            }
        }

        /// <summary>在 WKWebView 中加载指定 URL。</summary>
        public static void LoadUrl(IntPtr webView, string url)
        {
            try
            {
                if (_clsNSURL == IntPtr.Zero || _clsNSURLRequest == IntPtr.Zero)
                {
                    return;
                }
                var nsUrlStr = CreateNsString(url);
                if (nsUrlStr == IntPtr.Zero)
                {
                    return;
                }
                var nsUrl = SendPtr(Send2(_clsNSURL, Sel("alloc")), Sel("initWithString:"), nsUrlStr);
                CFRelease(nsUrlStr);
                if (nsUrl == IntPtr.Zero)
                {
                    return;
                }
                var request = SendPtr(Send2(_clsNSURLRequest, Sel("alloc")), Sel("initWithURL:"), nsUrl);
                ReleaseObject(nsUrl);
                if (request == IntPtr.Zero)
                {
                    return;
                }
                SendPtr(webView, Sel("loadRequest:"), request);
                ReleaseObject(request);
            }
            catch (Exception)
            {
                // 加载失败保持空白,用户可关闭窗口回退外部浏览器
            }
        }

        private static IntPtr CreateNsString(string value)
        {
            if (_clsNSString == IntPtr.Zero)
            {
                return IntPtr.Zero;
            }
            var cf = CFStringCreateWithCString(IntPtr.Zero, value, KCFStringEncodingUTF8);
            if (cf == IntPtr.Zero)
            {
                return IntPtr.Zero;
            }
            // CFString 与 NSString toll-free bridged,可直接作为 ObjC 对象使用
            return cf;
        }

        public static void ReleaseObject(IntPtr obj)
        {
            if (obj != IntPtr.Zero)
            {
                Send2(obj, Sel("release"));
            }
        }
    }
}
