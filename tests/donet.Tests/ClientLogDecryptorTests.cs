using donet.Core.Services.Gacha;

namespace donet.Tests;

public class ClientLogDecryptorTests
{
    [Fact]
    public void Decrypt_RoundTrip_RestoresPlainText()
    {
        string plain = "Hello, Wuthering Waves! 鸣潮日志测试 123";
        var plainBytes = System.Text.Encoding.UTF8.GetBytes(plain);

        // 加密(游戏侧)使用与解密相反的 key 映射:奇数→0xEF,偶数→0xA5
        // (解密: 奇数→0xA5, 偶数→0xEF; 两 key 低 4 位均为奇数,条件翻转后恰好还原)
        var encrypted = new byte[plainBytes.Length];
        for (int i = 0; i < plainBytes.Length; i++)
        {
            int b = plainBytes[i] & 0xFF;
            encrypted[i] = ((b & 0x0F) % 2) == 1 ? (byte)(b ^ 0xEF) : (byte)(b ^ 0xA5);
        }

        var decrypted = ClientLogDecryptor.Decrypt(encrypted);
        Assert.Equal(plain, decrypted);
    }

    [Fact]
    public void Decrypt_EmptyInput_ReturnsNull()
    {
        Assert.Null(ClientLogDecryptor.Decrypt([]));
    }
}
