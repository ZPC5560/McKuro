using System.Text;

namespace donet.Core.Services.Gacha;

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
        for (int i = 0; i < bytes.Length; i++)
        {
            int b = bytes[i] & 0xFF;
            buffer[i] = ((b & 0x0F) % 2) == 1 ? (byte)(b ^ 0xA5) : (byte)(b ^ 0xEF);
        }

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
            var bytes = File.ReadAllBytes(path);
            return Decrypt(bytes);
        }
        catch (Exception)
        {
            return null;
        }
    }
}
