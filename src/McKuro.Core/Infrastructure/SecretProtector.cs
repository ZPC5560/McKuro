using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace McKuro.Core.Infrastructure;

/// <summary>
/// 设置文件中敏感字段(token / 手机号 / 设备号 / 登录数据)的落盘加密。
/// <para>
/// Windows:DPAPI(CurrentUser 作用域),密文形如 <c>dpapi:v1:&lt;base64&gt;</c>,只有同一 Windows 用户能解开。
/// macOS / Linux:暂时仍明文保存。TODO: macOS 接入 Keychain、Linux 接入 libsecret。
/// </para>
/// <para>
/// 兼容旧数据:读取时不带前缀的值按明文原样返回,下次保存时自动写成密文(透明迁移)。
/// 解密失败(设置文件从别的电脑/别的用户拷来、或在非 Windows 上读到 Windows 密文)返回空串,
/// <b>绝不抛异常</b> —— 抛出会让整个 settings.json 反序列化失败、回退成默认值,丢掉全部设置。
/// </para>
/// </summary>
internal static class SecretProtector
{
    /// <summary>DPAPI 密文前缀(带版本号,便于以后换算法)。</summary>
    internal const string DpapiPrefix = "dpapi:v1:";

    // 反序列化/序列化在调用线程上同步执行,SettingsService 读写前后取差值即可得知本次失败数
    // (转换器由源生成器实例化,无法注入 logger)。
    [ThreadStatic] private static int _unprotectFailures;
    [ThreadStatic] private static int _protectFailures;

    /// <summary>取出并清零当前线程的解密失败计数。</summary>
    internal static int TakeUnprotectFailures()
    {
        var n = _unprotectFailures;
        _unprotectFailures = 0;
        return n;
    }

    /// <summary>取出并清零当前线程的加密失败计数(失败时该字段以明文落盘)。</summary>
    internal static int TakeProtectFailures()
    {
        var n = _protectFailures;
        _protectFailures = 0;
        return n;
    }

    /// <summary>明文 → 落盘值。空串原样返回;非 Windows 返回明文;加密失败返回明文并计数(不能因此丢数据)。</summary>
    internal static string Protect(string plain)
    {
        if (string.IsNullOrEmpty(plain) || plain.StartsWith(DpapiPrefix, StringComparison.Ordinal))
        {
            return plain;
        }
        if (!OperatingSystem.IsWindows())
        {
            // TODO: macOS Keychain / Linux libsecret。目前与旧版一致明文保存。
            return plain;
        }
        try
        {
            var cipher = Dpapi.Protect(Encoding.UTF8.GetBytes(plain));
            return DpapiPrefix + Convert.ToBase64String(cipher);
        }
        catch (Exception)
        {
            _protectFailures++;
            return plain;
        }
    }

    /// <summary>落盘值 → 明文。无前缀按旧版明文原样返回;解密失败返回空串并计数。</summary>
    internal static string Unprotect(string stored)
    {
        if (string.IsNullOrEmpty(stored) || !stored.StartsWith(DpapiPrefix, StringComparison.Ordinal))
        {
            return stored;
        }
        if (!OperatingSystem.IsWindows())
        {
            _unprotectFailures++;
            return "";
        }
        try
        {
            var cipher = Convert.FromBase64String(stored[DpapiPrefix.Length..]);
            return Encoding.UTF8.GetString(Dpapi.Unprotect(cipher));
        }
        catch (Exception)
        {
            _unprotectFailures++;
            return "";
        }
    }

    /// <summary>
    /// crypt32 DPAPI 的最小 P/Invoke 封装。
    /// 全部参数都是可直接传递的(blittable)类型:DllImport 无需运行时封送,AOT 友好,也不需要 AllowUnsafeBlocks。
    /// </summary>
    [SupportedOSPlatform("windows")]
    private static class Dpapi
    {
        private const int CRYPTPROTECT_UI_FORBIDDEN = 0x1;

        [StructLayout(LayoutKind.Sequential)]
        private struct DataBlob
        {
            public int cbData;
            public IntPtr pbData;
        }

        [DllImport("crypt32.dll", SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        private static extern int CryptProtectData(
            ref DataBlob pDataIn,
            IntPtr szDataDescr,
            IntPtr pOptionalEntropy,
            IntPtr pvReserved,
            IntPtr pPromptStruct,
            int dwFlags,
            ref DataBlob pDataOut);

        [DllImport("crypt32.dll", SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        private static extern int CryptUnprotectData(
            ref DataBlob pDataIn,
            IntPtr ppszDataDescr,
            IntPtr pOptionalEntropy,
            IntPtr pvReserved,
            IntPtr pPromptStruct,
            int dwFlags,
            ref DataBlob pDataOut);

        [DllImport("kernel32.dll")]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        private static extern IntPtr LocalFree(IntPtr hMem);

        internal static byte[] Protect(byte[] data) => Transform(data, protect: true);

        internal static byte[] Unprotect(byte[] data) => Transform(data, protect: false);

        private static byte[] Transform(byte[] data, bool protect)
        {
            var pin = GCHandle.Alloc(data, GCHandleType.Pinned);
            var output = default(DataBlob);
            try
            {
                var input = new DataBlob { cbData = data.Length, pbData = pin.AddrOfPinnedObject() };
                var ok = protect
                    ? CryptProtectData(ref input, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, CRYPTPROTECT_UI_FORBIDDEN, ref output)
                    : CryptUnprotectData(ref input, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, CRYPTPROTECT_UI_FORBIDDEN, ref output);
                if (ok == 0)
                {
                    throw new Win32Exception(Marshal.GetLastPInvokeError());
                }

                var result = new byte[output.cbData];
                if (output.cbData > 0)
                {
                    Marshal.Copy(output.pbData, result, 0, output.cbData);
                }
                return result;
            }
            finally
            {
                pin.Free();
                if (output.pbData != IntPtr.Zero)
                {
                    LocalFree(output.pbData);
                }
            }
        }
    }
}

/// <summary>
/// 敏感字符串字段的 JSON 转换器:写入时加密(见 <see cref="SecretProtector"/>),读取时解密;
/// 旧版明文值直接读入,下次保存即转为密文。
/// <para>以 <c>[JsonConverter(typeof(ProtectedStringConverter))]</c> 标在属性上,源生成的
/// <c>SettingsJsonContext</c> 会直接实例化它(无反射,AOT 可用)。</para>
/// </summary>
public sealed class ProtectedStringConverter : JsonConverter<string>
{
    public override string Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => SecretProtector.Unprotect(reader.GetString() ?? "");

    public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options)
        => writer.WriteStringValue(SecretProtector.Protect(value ?? ""));
}
