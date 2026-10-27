using System.Text;
using McKuro.Core.Services.Gacha;

namespace McKuro.Tests;

/// <summary>
/// 日志解密 + 有效性校验。
/// <para>
/// 校验的意义:逐字节异或解错密钥时<b>不会报错</b>,只产出乱码。早先调用方拿乱码去搜抽卡链接,
/// 搜不到就报「请先在游戏里打开抽卡页面」—— 而真实原因是游戏换了密钥,用户按提示做一百次也没用。
/// 下面这几个用例守的就是「密钥变更能被识别出来」。
/// </para>
/// </summary>
public class ClientLogDecryptorTests
{
    /// <summary>构造一份游戏侧的加密日志(BOM 开头 + 文本)。</summary>
    private static byte[] EncryptedGameLog(string text)
    {
        var plain = new List<byte> { 0xEF, 0xBB, 0xBF };
        plain.AddRange(Encoding.UTF8.GetBytes(text));
        var encrypted = new byte[plain.Count];
        for (int i = 0; i < plain.Count; i++)
        {
            int b = plain[i];
            // 游戏侧加密:明文奇数 → 0xEF,偶数 → 0xA5(与解密侧的映射互为逆)
            encrypted[i] = ((b & 1) == 1) ? (byte)(b ^ 0xEF) : (byte)(b ^ 0xA5);
        }
        return encrypted;
    }

    [Fact]
    public void Decrypt_RoundTrip_RestoresPlainText()
    {
        string plain = "Hello, Wuthering Waves! 鸣潮日志测试 123";
        var plainBytes = Encoding.UTF8.GetBytes(plain);

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

    [Fact]
    public void IsELogEncrypted_True_For_Encrypted_Content()
    {
        // 加密内容不以 BOM 开头(BOM 三字节被翻转了 bit0)
        var encrypted = EncryptedGameLog("Log file open");
        Assert.True(ClientLogDecryptor.IsELogEncrypted(encrypted));
    }

    [Fact]
    public void IsELogEncrypted_False_For_Plain_Content_With_Bom()
    {
        var plain = new List<byte> { 0xEF, 0xBB, 0xBF };
        plain.AddRange(Encoding.UTF8.GetBytes("already plain"));

        Assert.False(ClientLogDecryptor.IsELogEncrypted(plain.ToArray()));
    }

    [Fact]
    public void IsValidDecryptedLog_True_For_Real_Plain_Log()
    {
        var plain = new List<byte> { 0xEF, 0xBB, 0xBF };
        plain.AddRange(Encoding.UTF8.GetBytes("Log file open, 10/27/26 09:00:00\n抽卡记录 https://example.com"));

        Assert.True(ClientLogDecryptor.IsValidDecryptedLog(plain.ToArray()));
    }

    [Fact]
    public void IsValidDecryptedLog_False_When_Decrypted_Text_Is_Structure_Broken()
    {
        // 模拟"密钥变更把字节结构打坏":取一份合法加密日志,翻转其中一个字节,
        // 解密后该位置的 UTF-8 多字节序列就不连续了。
        // 这正是 `IsValidDecryptedLog` 要拦的情况(密钥变更最常见的结果)。
        var encrypted = EncryptedGameLog("Log file open, 10/27/26 09:00:00 鸣潮抽卡记录");
        encrypted[^2] ^= 0xFF;

        var decryptedWrong = new byte[encrypted.Length];
        for (int i = 0; i < encrypted.Length; i++)
        {
            var b = encrypted[i];
            decryptedWrong[i] = (b & 1) == 1 ? (byte)(b ^ 0xA5) : (byte)(b ^ 0xEF);
        }

        Assert.False(ClientLogDecryptor.IsValidDecryptedLog(decryptedWrong));
    }

    [Fact]
    public void IsValidDecryptedLog_Detects_KeyMismatch_That_Breaks_Utf8()
    {
        // 游戏把偶数位的密钥从 0xA5 换成 0xDF:解密后每个偶数位明文字节都被异或 0x7A,
        // 大量字节落进非法 UTF-8 区间 → 必须被判为无效。
        // (注:若换成的密钥只差低位、恰好仍然是合法 UTF-8 码点,这种校验抓不到 ——
        //  那属于极端巧合,见 ClientLogDecryptor 类注释里对校验能力的说明。)
        var plain = new List<byte> { 0xEF, 0xBB, 0xBF };
        plain.AddRange(Encoding.UTF8.GetBytes("Log file open 鸣潮抽卡记录日志 09:00:00"));
        var wrongKeyEncrypted = new byte[plain.Count];
        for (int i = 0; i < plain.Count; i++)
        {
            var b = plain[i];
            wrongKeyEncrypted[i] = (b & 1) == 1 ? (byte)(b ^ 0xEF) : (byte)(b ^ 0xDF);
        }

        // 用"正确"的算法解一份"用错误密钥加密"的内容
        var decryptedWrong = new byte[wrongKeyEncrypted.Length];
        for (int i = 0; i < wrongKeyEncrypted.Length; i++)
        {
            var b = wrongKeyEncrypted[i];
            decryptedWrong[i] = (b & 1) == 1 ? (byte)(b ^ 0xA5) : (byte)(b ^ 0xEF);
        }

        Assert.False(ClientLogDecryptor.IsValidDecryptedLog(decryptedWrong));
    }

    [Fact]
    public void IsValidDecryptedLog_False_Without_Bom()
    {
        var noBom = Encoding.UTF8.GetBytes("Log file open but no BOM");

        Assert.False(ClientLogDecryptor.IsValidDecryptedLog(noBom));
    }

    [Fact]
    public void DecryptFileChecked_Reports_KeyMismatch_Instead_Of_Silent_Garbage()
    {
        var path = Path.Combine(Path.GetTempPath(), "McKuro-elog-" + Guid.NewGuid().ToString("N") + ".log");
        try
        {
            // 造一份"用另一套密钥加密"的日志:解密后是乱码,但文件本身非空
            var plain = new List<byte> { 0xEF, 0xBB, 0xBF };
            plain.AddRange(Encoding.UTF8.GetBytes("Log file open 鸣潮日志"));
            var wrongKeyEncrypted = new byte[plain.Count];
            for (int i = 0; i < plain.Count; i++)
            {
                var b = plain[i];
                wrongKeyEncrypted[i] = (b & 1) == 1 ? (byte)(b ^ 0xEF) : (byte)(b ^ 0xDF);
            }
            File.WriteAllBytes(path, wrongKeyEncrypted);

            var result = ClientLogDecryptor.DecryptFileChecked(path);

            Assert.Equal(ClientLogDecryptStatus.KeyMismatch, result.Status);
            Assert.Null(result.Text);
            Assert.False(result.IsSuccess);
        }
        finally
        {
            try { File.Delete(path); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void DecryptFileChecked_Succeeds_On_Valid_Encrypted_Log()
    {
        var path = Path.Combine(Path.GetTempPath(), "McKuro-elog-ok-" + Guid.NewGuid().ToString("N") + ".log");
        try
        {
            File.WriteAllBytes(path, EncryptedGameLog("Log file open\n抽卡记录链接 https://example.com/x"));

            var result = ClientLogDecryptor.DecryptFileChecked(path);

            Assert.Equal(ClientLogDecryptStatus.Success, result.Status);
            Assert.Contains("抽卡记录链接", result.Text);
        }
        finally
        {
            try { File.Delete(path); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void DecryptFileChecked_Reports_FileMissing()
    {
        var result = ClientLogDecryptor.DecryptFileChecked(
            Path.Combine(Path.GetTempPath(), "McKuro-elog-missing-" + Guid.NewGuid().ToString("N") + ".log"));

        Assert.Equal(ClientLogDecryptStatus.FileMissing, result.Status);
        Assert.False(result.IsSuccess);
    }
}
