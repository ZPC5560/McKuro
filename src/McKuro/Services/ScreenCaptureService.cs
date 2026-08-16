using System.Runtime.InteropServices;
using McKuro.Services;

#pragma warning disable CA1416 // System.Drawing 仅 Windows 使用,调用点已在 #if WINDOWS 内

namespace McKuro.Services;

/// <summary>
/// 快捷键截图服务:注册全局热键,按下后截取整个屏幕并保存 PNG。
/// <para>参考 Haiyu 的 ScreenCaptureService(RegisterHotKey + WinUIEx 消息监控)。</para>
/// </summary>
public sealed class ScreenCaptureService : IDisposable
{
    private const int HotkeyId = 141;

    private nint _hwnd;
    private WindowMessageMonitor? _monitor;
    private bool _registered;

    /// <summary>截图完成(参数为保存路径)。</summary>
    public event Action<string>? CaptureCompleted;

    /// <summary>绑定到主窗口句柄,开始监听热键消息。</summary>
    public void Attach(nint hwnd)
    {
        _hwnd = hwnd;
        _monitor = new WindowMessageMonitor(hwnd);
        _monitor.HotkeyReceived += OnHotkeyReceived;
    }

    /// <summary>注册全局热键(modifier: Win/Ctrl/Alt/Shift;key: 按键名如 F8)。</summary>
    public bool Register(string modifierKey, string key)
    {
        Unregister();

        uint modifier = modifierKey.ToLowerInvariant() switch
        {
            "ctrl" => ModControl,
            "alt" => ModAlt,
            "shift" => ModShift,
            _ => ModWin,
        };
        uint vk = key.ToLowerInvariant() switch
        {
            "f1" => 0x70, "f2" => 0x71, "f3" => 0x72, "f4" => 0x73,
            "f5" => 0x74, "f6" => 0x75, "f7" => 0x76, "f8" => 0x77,
            "f9" => 0x78, "f10" => 0x79, "f11" => 0x7A, "f12" => 0x7B,
            _ => 0x77, // 默认 F8
        };

        _registered = NativeMethods.RegisterHotKey(_hwnd, HotkeyId, modifier, vk);
        return _registered;
    }

    public void Unregister()
    {
        if (_registered && _hwnd != nint.Zero)
        {
            NativeMethods.UnregisterHotKey(_hwnd, HotkeyId);
            _registered = false;
        }
    }

    private void OnHotkeyReceived()
    {
        var settings = AppServices.Settings.Current;
        if (!settings.CaptureEnabled)
        {
            return;
        }

        var path = CaptureToFile(GetCaptureDirectory(settings));
        if (path is not null)
        {
            CaptureCompleted?.Invoke(path);
        }
    }

    private static string GetCaptureDirectory(McKuro.Core.Services.Settings.AppSettings settings)
    {
        if (!string.IsNullOrWhiteSpace(settings.ScreenCapturesDir))
        {
            return settings.ScreenCapturesDir;
        }
        var pictures = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
        return string.IsNullOrEmpty(pictures)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Pictures", "McKuro")
            : Path.Combine(pictures, "McKuro");
    }

    /// <summary>截取全屏并保存 PNG,返回文件路径;失败返回 null。</summary>
    public string? CaptureToFile(string directory)
    {
#if WINDOWS
        try
        {
            Directory.CreateDirectory(directory);
            var width = NativeMethods.GetSystemMetrics(NativeMethods.SmCxScreen);
            var height = NativeMethods.GetSystemMetrics(NativeMethods.SmCyScreen);
            if (width <= 0 || height <= 0)
            {
                return null;
            }
            var fileName = DateTime.Now.ToString("yyyyMMddHHmmssff") + ".png";
            var path = Path.Combine(directory, fileName);
            using var bitmap = new System.Drawing.Bitmap(width, height);
            using (var g = System.Drawing.Graphics.FromImage(bitmap))
            {
                g.CopyFromScreen(0, 0, 0, 0, new System.Drawing.Size(width, height));
            }
            bitmap.Save(path, System.Drawing.Imaging.ImageFormat.Png);
            return path;
        }
        catch (Exception)
        {
            return null;
        }
#else
        return null;
#endif
    }

    public void Dispose()
    {
        Unregister();
        _monitor?.Dispose();
        _monitor = null;
    }

    private const uint ModAlt = 0x0001;
    private const uint ModControl = 0x0002;
    private const uint ModShift = 0x0004;
    private const uint ModWin = 0x0008;
}

/// <summary>主窗口消息监控:子类化 WndProc 拦截 WM_HOTKEY(参考 WinUIEx WindowMessageMonitor)。</summary>
internal sealed class WindowMessageMonitor : IDisposable
{
    private const int WmHotkey = 0x0312;
    private const int GwlWndProc = -4;

    private readonly nint _hwnd;
    private nint _oldWndProc;
    private NativeMethods.WndProcDelegate _wndProcDelegate = null!;

    public event Action? HotkeyReceived;

    public WindowMessageMonitor(nint hwnd)
    {
        _hwnd = hwnd;
        _wndProcDelegate = WndProc;
        _oldWndProc = NativeMethods.SetWindowLongPtr(hwnd, GwlWndProc, Marshal.GetFunctionPointerForDelegate(_wndProcDelegate));
    }

    private nint WndProc(nint hwnd, uint msg, nint wParam, nint lParam)
    {
        if (msg == WmHotkey && wParam == 141)
        {
            HotkeyReceived?.Invoke();
        }
        return NativeMethods.CallWindowProc(_oldWndProc, hwnd, msg, wParam, lParam);
    }

    public void Dispose()
    {
        if (_oldWndProc != nint.Zero)
        {
            NativeMethods.SetWindowLongPtr(_hwnd, GwlWndProc, _oldWndProc);
            _oldWndProc = nint.Zero;
        }
    }
}

internal static class NativeMethods
{
    public const int SmCxScreen = 0;
    public const int SmCyScreen = 1;

    public delegate nint WndProcDelegate(nint hWnd, uint msg, nint wParam, nint lParam);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool RegisterHotKey(nint hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool UnregisterHotKey(nint hWnd, int id);

    [DllImport("user32.dll")]
    public static extern nint SetWindowLongPtr(nint hWnd, int nIndex, nint dwNewLong);

    [DllImport("user32.dll")]
    public static extern nint CallWindowProc(nint lpPrevWndFunc, nint hWnd, uint msg, nint wParam, nint lParam);

    [DllImport("user32.dll")]
    public static extern int GetSystemMetrics(int nIndex);
}
