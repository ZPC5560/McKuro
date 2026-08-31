using System.Text;

namespace McKuro.Core.Services.Gacha;

/// <summary>
/// 解密鸣潮客户端日志文件(Client.log)。
/// <para>
/// 加密规则(源自 WutheringWavesTool):
/// 逐字节处理:若 (byte &amp; 0x0F) % 2 == 1 则异或 0xA5,否则异或 0xEF。
/// </para>
/// </summary>
public static class ClientLogDecryptor
{
    public static string? Decrypt(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length == 0)
        {
            return null;
        }

        var buffer = new byte[bytes.Length];
        bytes.CopyTo(buffer);
        DecryptInPlace(buffer);
        return Encoding.UTF8.GetString(buffer);
    }

    public static string? DecryptFile(string path)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            // 原地解密复用 ReadAllBytes 的数组:游戏日志可达数十 MB,
            // 旧实现(读入 + 新缓冲 + GetString)瞬时驻留 3 倍文件体积。
            var bytes = File.ReadAllBytes(path);
            DecryptInPlace(bytes);
            return Encoding.UTF8.GetString(bytes);
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>逐字节异或解密(原地):若 (byte &amp; 0x0F) % 2 == 1 则异或 0xA5,否则异或 0xEF。
    /// (b &amp; 0x0F) % 2 等价于取 bit0,用位与替代除法。</summary>
    private static void DecryptInPlace(Span<byte> buffer)
    {
        for (int i = 0; i < buffer.Length; i++)
        {
            ref byte b = ref buffer[i];
            b = (b & 1) == 1 ? (byte)(b ^ 0xA5) : (byte)(b ^ 0xEF);
        }
    }
}
