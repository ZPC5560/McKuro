using System.Text;
using System.Text.Unicode;

namespace McKuro.Core.Services.Gacha;

/// <summary>解密结果状态(区分「文件没有」/「密钥变了」/「内容损坏」,便于给用户可操作的提示)。</summary>
public enum ClientLogDecryptStatus
{
    /// <summary>解密成功,文本可用。</summary>
    Success,

    /// <summary>文件不存在。</summary>
    FileMissing,

    /// <summary>文件读不出来(被游戏占用 / 权限不足 / IO 错误)。</summary>
    ReadFailed,

    /// <summary>
    /// 密钥不匹配(或内容损坏):解出来不是合法 UTF-8 明文日志。
    /// <para>
    /// 这是**唯一需要告诉用户「请更新 McKuro」**的状态 —— 游戏改了加密常量,
    /// 本版本的解密算法解不开了,和"用户没在游戏里打开过抽卡页面"完全不是一回事。
    /// </para>
    /// </summary>
    KeyMismatch,
}

/// <summary>解密结果。</summary>
public readonly record struct ClientLogDecryptResult(ClientLogDecryptStatus Status, string? Text)
{
    public bool IsSuccess => Status == ClientLogDecryptStatus.Success && Text is not null;
}

/// <summary>
/// 解密鸣潮客户端日志文件(Client.log)。
/// <para>
/// 加密规则(源自 WutheringWavesTool):逐字节处理 —— 若 <c>byte &amp; 1 == 1</c> 则异或 <c>0xA5</c>,
/// 否则异或 <c>0xEF</c>。(原式 <c>(byte &amp; 0x0F) % 2</c> 等价于取 bit0。)
/// </para>
/// <para>
/// <b>为什么需要「校验解密结果」:</b>解密是逐字节异或,<b>密钥不匹配时不会报错</b> ——
/// 它只会安静地产出乱码。早先调用方拿到的乱码去搜抽卡链接,自然搜不到,于是统一报成
/// 「日志中未找到抽卡记录链接(请先在游戏中打开一次抽卡记录页面)」——
/// 而真正的原因是游戏换了加密常量,用户按这个提示去开一百次抽卡页面也没用。
/// 因此这里补两重校验(见 <see cref="IsELogEncrypted"/> / <see cref="IsValidDecryptedLog"/>),
/// 把「密钥变了」和「日志里确实没有链接」区分开,给不同的提示。
/// </para>
/// </summary>
public static class ClientLogDecryptor
{
    /// <summary>明文日志的 UTF-8 BOM(游戏写日志时带)。</summary>
    private static readonly byte[] Bom = [0xEF, 0xBB, 0xBF];

    /// <summary>严格 UTF-8 解码器:非法序列抛异常而非替换成 U+FFFD(校验用)。</summary>
    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    /// <summary>
    /// 判断日志内容是否为 ELog 加密状态。
    /// <para>
    /// 依据:明文日志以 UTF-8 BOM 开头;而 BOM 三个字节在 xor 后不可能仍是 BOM
    /// (两个密钥 <c>0xA5</c>/<c>0xEF</c> 的低位都是 1,加密会翻转每字节的 bit0,
    /// BOM 的三字节 bit0 全被翻掉,结果必然不是 <c>EF BB BF</c>)。
    /// 该判定与具体密钥取值无关,换 key 后依然成立。
    /// </para>
    /// </summary>
    public static bool IsELogEncrypted(ReadOnlySpan<byte> data) =>
        data.Length >= 3 && !data[..3].SequenceEqual(Bom);

    /// <summary>
    /// 校验解密结果是否是一份有效的明文日志:BOM 开头 + 其后全部是合法 UTF-8。
    /// <para>
    /// 密钥不匹配时会翻转约一半字节的 bit0,破坏多字节 UTF-8 序列的连续性,
    /// 因此「BOM + 严格 UTF-8」两道校验即可可靠识别密钥变更。
    /// </para>
    /// </summary>
    public static bool IsValidDecryptedLog(ReadOnlySpan<byte> decrypted)
    {
        if (decrypted.Length < 4 || !decrypted[..3].SequenceEqual(Bom))
        {
            return false;
        }

        // 严格校验 BOM 之后的内容:任何非法 UTF-8 序列都判为无效。
        // (Utf8.IsValid 返回 bool,不要写成丢弃返回值 —— 那样等于只校验了 BOM。)
        return Utf8.IsValid(decrypted[3..]);
    }

    /// <summary>解密字节数组;空输入返回 null。</summary>
    public static string? Decrypt(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length == 0)
        {
            return null;
        }

        var buffer = new byte[bytes.Length];
        bytes.CopyTo(buffer);
        TransformInPlace(buffer);
        return Encoding.UTF8.GetString(buffer);
    }

    /// <summary>
    /// 解密文件(旧签名,保留兼容)。<b>不区分失败原因</b> —— 需要给用户准确提示时用
    /// <see cref="DecryptFileChecked"/>。
    /// </summary>
    public static string? DecryptFile(string path) =>
        DecryptFileChecked(path).Text;

    /// <summary>
    /// 解密文件并区分失败原因。调用方据此决定提示文案:
    /// <see cref="ClientLogDecryptStatus.KeyMismatch"/> 要提示更新软件,
    /// 而「解密成功但没搜到链接」才是提示用户去游戏里打开抽卡页面。
    /// </summary>
    public static ClientLogDecryptResult DecryptFileChecked(string path)
    {
        byte[] bytes;
        try
        {
            if (!File.Exists(path))
            {
                return new ClientLogDecryptResult(ClientLogDecryptStatus.FileMissing, null);
            }
            // 原地解密复用读入的数组:游戏日志可达数十 MB,
            // 旧实现(读入 + 新缓冲 + GetString)瞬时驻留 3 倍文件体积。
            bytes = ReadAllBytesShared(path);
        }
        catch (Exception)
        {
            return new ClientLogDecryptResult(ClientLogDecryptStatus.ReadFailed, null);
        }

        if (bytes.Length == 0)
        {
            return new ClientLogDecryptResult(ClientLogDecryptStatus.ReadFailed, null);
        }

        // 已经是明文(旧版日志 / 游戏某次改成不加密):不再异或,只校验能不能当文本用。
        if (!IsELogEncrypted(bytes))
        {
            return Decode(bytes);
        }

        TransformInPlace(bytes);
        return Decode(bytes);
    }

    /// <summary>
    /// 以 <c>FileShare.ReadWrite | FileShare.Delete</c> 读取整个文件。
    /// <para>
    /// <see cref="File.ReadAllBytes"/> 只允许 <c>FileShare.Read</c>,游戏运行中(持有写句柄)打开会共享冲突,
    /// 而"游戏开着时同步抽卡"恰恰是最常见的用法。按打开时的长度读一份快照;
    /// 读的过程中文件被截短则按实际读到的长度返回。
    /// </para>
    /// </summary>
    private static byte[] ReadAllBytesShared(string path)
    {
        using var fs = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            bufferSize: 1,
            FileOptions.SequentialScan);
        long length = fs.Length;
        if (length > Array.MaxLength)
        {
            throw new IOException("Client.log too large");
        }

        var buffer = new byte[length];
        int read = fs.ReadAtLeast(buffer, buffer.Length, throwOnEndOfStream: false);
        if (read < buffer.Length)
        {
            Array.Resize(ref buffer, read);
        }
        return buffer;
    }

    /// <summary>
    /// 末尾不完整 UTF-8 序列的字节数(0~3)。游戏边跑边写时,读到的快照可能恰好截在一个多字节字符中间,
    /// 这种截断不代表密钥变了,校验前去掉即可。只处理"合法首字节 + 不足数量的续字节"这一种形态。
    /// </summary>
    private static int IncompleteUtf8TailLength(ReadOnlySpan<byte> bytes)
    {
        for (int back = 1; back <= 3 && back <= bytes.Length; back++)
        {
            byte b = bytes[^back];
            if ((b & 0xC0) == 0x80)
            {
                continue; // 续字节,继续往前找首字节
            }

            int need = (b & 0xE0) == 0xC0 ? 2 : (b & 0xF0) == 0xE0 ? 3 : (b & 0xF8) == 0xF0 ? 4 : 1;
            return need > back ? back : 0;
        }
        return 0;
    }

    /// <summary>
    /// 校验 + 解码一步到位。校验不过(含密钥不匹配)统一给
    /// <see cref="ClientLogDecryptStatus.KeyMismatch"/> —— 对用户而言下一步动作相同(升级软件)。
    /// </summary>
    private static ClientLogDecryptResult Decode(byte[] bytes)
    {
        int length = bytes.Length - IncompleteUtf8TailLength(bytes);
        if (!IsValidDecryptedLog(bytes.AsSpan(0, length)))
        {
            return new ClientLogDecryptResult(ClientLogDecryptStatus.KeyMismatch, null);
        }

        var text = DecodeStrict(bytes, length);
        return text is null
            ? new ClientLogDecryptResult(ClientLogDecryptStatus.KeyMismatch, null)
            : new ClientLogDecryptResult(ClientLogDecryptStatus.Success, text);
    }

    /// <summary>
    /// 严格 UTF-8 解码(跳过 BOM)。
    /// <para>
    /// 理论上调用前已由 <see cref="IsValidDecryptedLog"/> 校验过不会抛;但仍然兜一层 try:
    /// 日志是**外部可变文件**(游戏边跑边写),读进来与解码之间内容可能已被追写,
    /// 抛出未捕获异常会把「游玩统计/抽卡同步」整条链路打成崩溃,代价远大于多一次 catch。
    /// </para>
    /// </summary>
    private static string? DecodeStrict(byte[] bytes, int length)
    {
        try
        {
            return StrictUtf8.GetString(bytes, 3, length - 3);
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// 逐字节异或(原地,自逆):<c>bit0 == 1</c> 时异或 <c>0xA5</c>,否则异或 <c>0xEF</c>。
    /// 用位与取 bit0 代替原来的 <c>(b &amp; 0x0F) % 2</c>,语义等价、少一次取模。
    /// </summary>
    private static void TransformInPlace(Span<byte> buffer)
    {
        for (int i = 0; i < buffer.Length; i++)
        {
            ref byte b = ref buffer[i];
            b = (b & 1) == 1 ? (byte)(b ^ 0xA5) : (byte)(b ^ 0xEF);
        }
    }
}
