using System.Collections.Concurrent;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using McKuro.Core.Models.Roles;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace McKuro.Services;

/// <summary>
/// 角色图标磁盘持久化缓存:库街区正常时把角色详情图标缓存到 <c>icon_cache/</c>,
/// mcguide 攻略站兜底时按名称复用缓存图标,避免切换数据源(A/B 域名不同)导致的图标缺失/错位。
/// <para>
/// 目录结构:<c>icon_cache/index.json</c> 记录 category → name → 本地文件路径,
/// 图标存 <c>icon_cache/{category}/{safe(name)}.png</c>(category ∈ role/weapon/skill/echo/chain/attr)。
/// 文件名按名称清洗非法字符;单图标下载失败静默,不影响主流程。
/// 写入侧的三道约束:分类名必须过 <see cref="IsSafeCategory"/> 校验(防 <c>..</c>/分隔符经
/// <c>Path.Combine</c> 逃逸缓存目录)、下载按 <see cref="MaxDownloadBytes"/> 流式限长(防超大响应 OOM)、
/// 落盘走「临时文件 + File.Move 覆盖」原子替换(防崩溃留下半截 PNG 被永久命中)。</para>
/// </summary>
public sealed class IconDiskCacheService
{
    public const string CategoryRole = "role";
    public const string CategoryWeapon = "weapon";
    public const string CategorySkill = "skill";
    public const string CategoryEcho = "echo";
    public const string CategoryChain = "chain";
    public const string CategoryAttr = "attr";
    /// <summary>玩家头像(首页资料卡;参照 Java WutheringWavesTool:接口取 URL → 落盘 assets/header,UI 只加载本地文件)。</summary>
    public const string CategoryAvatar = "avatar";

    /// <summary>
    /// 玩家头像的缓存键(<b>唯一权威来源</b>)。
    /// <para>
    /// 头像属于"游戏角色"而不是"库街区账号":一个账号可以有多个角色,按账号缓存会在切换角色时串头像,
    /// 故键取<b>游戏角色 UID</b>;拿不到角色 UID 时退化为头像 URL 的文件名。
    /// </para>
    /// <para>
    /// 必须由读、写两侧共用:写入侧(<c>HomeViewModel.ResolveAvatarAsync</c>)与读取侧
    /// (同文件的预填 + <c>MainWindowViewModel</c> 的启动占位)只要有<em>一处</em>用了别的键,
    /// 缓存就会永久命中不了 —— 曾因写入改 roleId、读取仍按 userId 而使导航栏头像启动占位失效。
    /// </para>
    /// </summary>
    public static string AvatarCacheKey(string? roleId, string? urlOrFallback)
        => Safe(!string.IsNullOrEmpty(roleId) ? roleId : (urlOrFallback ?? ""));

    /// <summary>
    /// 单图标下载的硬上限(字节,10MB):图标 PNG 远小于此值,超过即视为异常/恶意响应。
    /// 必须在下载阶段用流式读取拦住 —— 白图/解码守卫(<see cref="LooksLikeBlankOrWhite"/>)
    /// 要等全量字节到手才生效,拦不住「超大响应先把内存吃满」这一路。
    /// </summary>
    public const long MaxDownloadBytes = 10L * 1024 * 1024;

    private readonly string _cacheDir;
    private readonly string _indexPath;
    private readonly Func<string, CancellationToken, Task<byte[]?>> _download;
    private readonly ILogger _logger;
    private readonly object _lock = new();
    private readonly Dictionary<string, Dictionary<string, string>> _index = new(StringComparer.Ordinal);

    /// <summary>
    /// 在途下载登记:key = category + '\n' + name + '\n' + url。
    /// 「先查缓存再下载」是竞态检查(两个并发请求都可能在对方落盘前判定未命中),
    /// 这里让同一 key 的并发请求共享同一次下载,任务结束(成功/失败)即移除。
    /// </summary>
    private readonly ConcurrentDictionary<string, Task> _inFlight = new(StringComparer.Ordinal);

    /// <param name="cacheDir">缓存目录;缺省为 <c>%AppData%\McKuro\icon_cache</c>。</param>
    /// <param name="download">下载委托(可注入便于测试);缺省用 <see cref="AppServices.Http"/> 下载字节。</param>
    /// <param name="logger">可选日志(记录被白图防御拦下的图标);缺省不记录。</param>
    public IconDiskCacheService(
        string? cacheDir = null,
        Func<string, CancellationToken, Task<byte[]?>>? download = null,
        ILogger<IconDiskCacheService>? logger = null)
    {
        _cacheDir = cacheDir ?? Path.Combine(AppServices.AppDataDir, "icon_cache");
        _indexPath = Path.Combine(_cacheDir, "index.json");
        _download = download ?? DefaultDownload;
        _logger = logger ?? NullLogger<IconDiskCacheService>.Instance;
        LoadIndex();
    }

    /// <summary>
    /// 默认下载:用应用共享 HttpClient 以 <see cref="HttpCompletionOption.ResponseHeadersRead"/> 流式拉取,
    /// 并施加 <see cref="MaxDownloadBytes"/> 硬上限(声明长度超限或实际流读超限都立即中止并返回 null,
    /// 避免超大响应把内存吃满)。非成功状态码返回 null;网络异常仍抛出,由调用方静默处理。
    /// </summary>
    private static async Task<byte[]?> DefaultDownload(string url, CancellationToken ct)
    {
        using var response = await AppServices.Http
            .GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct)
            .ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }
        await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        return await ReadCappedAsync(stream, response.Content.Headers.ContentLength, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// 把流读成字节数组,并按 <see cref="MaxDownloadBytes"/> 硬上限拦截:
    /// 声明长度(Content-Length)超限直接放弃(连流都不读);谎报/未声明时按实际累计字节数超限中止。
    /// 任一超限返回 <c>null</c>,调用方按「下载失败」静默跳过。
    /// 纯托管且不依赖网络,便于单测直接喂任意大小的流验证上限。
    /// </summary>
    public static async Task<byte[]?> ReadCappedAsync(Stream stream, long? declaredLength, CancellationToken ct = default)
    {
        if (declaredLength > MaxDownloadBytes)
        {
            return null;
        }

        // 声明了合法长度则按它预分配;未声明则从 64KB 起按需增长(累计字节仍受上限约束)
        using var buffer = new MemoryStream(declaredLength is > 0 ? (int)declaredLength.Value : 64 * 1024);
        var chunk = new byte[64 * 1024];
        long total = 0;
        int read;
        while ((read = await stream.ReadAsync(chunk, ct).ConfigureAwait(false)) > 0)
        {
            total += read;
            if (total > MaxDownloadBytes)
            {
                return null; // 服务端未声明或谎报 Content-Length:读超即时中止,不缓冲全量
            }
            buffer.Write(chunk, 0, read);
        }

        return buffer.ToArray();
    }

    /// <summary>
    /// 缓存角色详情的所有图标:遍历角色立绘/武器/技能/声骸/属性,
    /// 对每个 (category, name, url):url 为 http 且本地无该 name 缓存时下载落盘并更新索引。
    /// 下载失败静默(不抛)。<b>共鸣链(chain)不参与预下载</b>(白线稿问题未解决前不进磁盘缓存,见 <see cref="EnumerateIcons"/>)。
    /// </summary>
    public async Task CacheRoleIconsAsync(RoleDetail role, CancellationToken ct = default)
    {
        if (role is null)
        {
            return;
        }
        foreach (var (category, name, url) in EnumerateIcons(role))
        {
            if (ct.IsCancellationRequested)
            {
                return;
            }
            // 索引在循环结束后统一落盘(逐图标 SaveIndex 会把整个字典反复全量序列化)
            await CacheOneAsync(category, name, url, saveIndex: false, ct).ConfigureAwait(false);
        }
        SaveIndex();
    }

    /// <summary>
    /// 单个图标「查缓存 → 下载 → 落盘」的统一入口(所有写路径都收敛到这里):
    /// 非法分类 / 空名 / 非 http 地址 / 已缓存 / 下载失败一律静默跳过,不把异常抛给调用方。
    /// 同一 (category, name, url) 的并发请求共享同一次在途下载(<c>_inFlight</c>),避免
    /// check-then-download 竞态下的重复拉取与重复写盘。
    /// </summary>
    private async Task CacheOneAsync(string category, string name, string url, bool saveIndex, CancellationToken ct)
    {
        if (!IsSafeCategory(category))
        {
            // 非法分类经 Path.Combine 会逃出缓存目录:连下载都不发起(与 PurgeCategory 同类防御)
            _logger.LogDebug("图标分类名非法,已跳过磁盘缓存: category={Category} name={Name}", category, name);
            return;
        }
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(url) || !IsHttpUrl(url)
            || GetCachedIconPath(category, name) is not null)
        {
            return; // 空名称/非 http(本地路径)或已缓存,跳过
        }

        var key = InFlightKey(category, name, url);
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var running = _inFlight.GetOrAdd(key, tcs.Task);
        if (!ReferenceEquals(running, tcs.Task))
        {
            await running.ConfigureAwait(false); // 已有在途下载:等它完成即可(结果由其写入索引)
            return;
        }

        try
        {
            // 双检:从「查缓存」到「登记在途」之间可能被别的请求抢先写完(它的登记也已移除),
            // 此时命中缓存就不再重复下载 —— 这一步才真正闭合 check-then-act 的竞态窗口。
            if (GetCachedIconPath(category, name) is null)
            {
                await DownloadAndStoreAsync(category, name, url, saveIndex, ct).ConfigureAwait(false);
            }
        }
        finally
        {
            _inFlight.TryRemove(key, out _);
            tcs.TrySetResult(); // 异常/取消路径同样放行等待方,避免其悬挂
        }
    }

    /// <summary>在途下载去重键(category/name/url 三段,以换行分隔避免拼接歧义)。</summary>
    private static string InFlightKey(string category, string name, string url)
        => string.Concat(category, "\n", name, "\n", url);

    /// <summary>下载 + 落盘:任何失败(网络 / 白图拦截 / 写盘)都静默吞掉,保持「单图标失败不影响主流程」的语义。</summary>
    private async Task DownloadAndStoreAsync(string category, string name, string url, bool saveIndex, CancellationToken ct)
    {
        try
        {
            var bytes = await _download(url, ct).ConfigureAwait(false);
            if (bytes is null || bytes.Length == 0)
            {
                return;
            }
            if (Store(category, name, bytes) && saveIndex)
            {
                SaveIndex();
            }
        }
        catch (Exception)
        {
            // 单图标下载/写盘失败静默,不影响其他图标与主流程
        }
    }

    /// <summary>查询本地缓存图标路径(做存在性校验);无缓存返回 null。</summary>
    public string? GetCachedIconPath(string category, string name)
    {
        if (string.IsNullOrWhiteSpace(category) || string.IsNullOrWhiteSpace(name))
        {
            return null;
        }
        string? path;
        lock (_lock)
        {
            if (!_index.TryGetValue(category, out var map)
                || !map.TryGetValue(name, out path)
                || string.IsNullOrEmpty(path))
            {
                return null;
            }
        }
        // 存在性校验放锁外:File.Exists 是磁盘系统调用,不应在全局锁内串行化所有图标解析。
        return File.Exists(path) ? path : null;
    }

    /// <summary>
    /// 缓存任意 http 图片 URL 到磁盘(对齐 Java WutheringWavesTool <c>LocalResourcesManager.imageBuffer</c>:
    /// 已有本地文件直接复用,未命中才下载落盘)。用于玩家头像等按稳定 key(如 userId)缓存的图片。
    /// 非 http 地址/已缓存/下载失败均静默跳过;<b>分类名非法(含 <c>/</c> <c>\</c> <c>:</c> 或 <c>..</c>)同样直接拒绝</b>,
    /// 防止经 Path.Combine 写出缓存目录之外。统一存为 .png(库街区头像素材均为 png)。
    /// </summary>
    public Task CacheUrlAsync(string category, string name, string url, CancellationToken ct = default)
        => CacheOneAsync(category, name, url, saveIndex: true, ct);

    /// <summary>
    /// 解析图标:有缓存→本地路径;否则→fallbackUrl(如保留 mcguide 的 B 域名 URL)。
    /// <para>
    /// <b>共鸣链(chain)例外:不使用本地磁盘缓存,直接返回 fallbackUrl。</b>
    /// 原因(实测):<c>%AppData%\McKuro\icon_cache\chain\</c> 下 72/72 个 PNG 全部被污染为纯白图
    /// (alpha 掩码与远端原图逐像素相同,仅 RGB 被替换为 #FFFFFF)。命中这类白图会把本来可用的
    /// guide-res 黑色图标 URL 换成不可见的白图,再叠加上底板颜色后浅色主题下彻底看不见。
    /// 因此 chain 分类一律保留传入的远端 URL,由远端原图负责渲染
    /// (白图防御见 <see cref="LooksLikeBlankOrWhite"/>,污染清理见 <see cref="PurgeCategory"/>;
    /// 预下载枚举 <see cref="EnumerateIcons"/> 也已同步跳过 chain,不再白拉一次)。
    /// </para>
    /// </summary>
    public string ResolveIcon(string category, string name, string fallbackUrl)
    {
        if (string.Equals(category, CategoryChain, StringComparison.Ordinal))
        {
            return fallbackUrl; // 白图污染:chain 不走本地缓存
        }
        return GetCachedIconPath(category, name) ?? fallbackUrl;
    }

    /// <summary>
    /// 判断 PNG 字节是否为「空图 / 纯白图」——缓存落盘前的防御(实测共鸣链图标被整体污染为纯白)。
    /// <para>
    /// 纯托管实现,无原生/平台依赖(不引用 Avalonia 渲染栈,可在单测中直接调用):
    /// PNG 签名 → 遍历 chunk(IHDR/PLTE/IDAT/IEND) → zlib inflate → 逐行反滤波 → 抽样像素。
    /// 判定规则:全透明 → 空图;存在不透明像素且全部近似白(RGB 均 ≥ 240)→ 纯白图。
    /// 即透明像素只是被排除在样本之外,并不作为「有内容」的证据:「白形状 + 透明背景」的
    /// 合法白线稿图标同样会被判成纯白图而<b>暂不入磁盘缓存</b>(现状取舍:宁可少缓存,
    /// 也不缓存下来后不可见;这类图标继续走远端 URL,与 <see cref="ResolveIcon"/> 的 chain 绕过策略一致)。
    /// 无法识别或不支持的格式(非 PNG、非 8 位色深、Adam7 隔行、损坏数据)一律返回 <c>false</c>,
    /// 即「不拦截」——避免因解码能力不足而误杀正常图标。
    /// </para>
    /// </summary>
    public static bool LooksLikeBlankOrWhite(byte[]? png)
    {
        if (!TryDecodePngRgba(png, out var width, out var height, out var rgba))
        {
            return false; // 非 PNG / 不支持的编码:不拦截
        }
        if (width <= 0 || height <= 0)
        {
            return true;
        }

        // 抽样:目标约 65536 个采样点(图标通常 ≤ 256×256,即全像素扫描),
        // 兼顾大图耗时与「白色底 + 细小深色轮廓」的检出率。
        var step = Math.Max(1, (int)Math.Sqrt((double)width * height / 65536.0));
        var opaque = 0;
        var allNearWhite = true;
        for (var y = 0; y < height; y += step)
        {
            for (var x = 0; x < width; x += step)
            {
                var i = (y * width + x) * 4;
                if (rgba[i + 3] <= 8)
                {
                    continue; // 透明像素不计入样本:判定只看「不透明像素是否全部近白」
                }
                opaque++;
                if (rgba[i] < 240 || rgba[i + 1] < 240 || rgba[i + 2] < 240)
                {
                    allNearWhite = false;
                }
            }
        }

        if (opaque == 0)
        {
            return true; // 全透明:空图
        }
        return allNearWhite; // 所有不透明像素都是近白色:纯白图
    }

    /// <summary>
    /// 反序列化 PNG 为 RGBA8888 像素缓冲;不支持的格式/损坏数据返回 false(调用方按「不拦截」处理)。
    /// </summary>
    private static bool TryDecodePngRgba(byte[]? png, out int width, out int height, out byte[] rgba)
    {
        width = 0;
        height = 0;
        rgba = [];
        if (png is null || png.Length < 8 + 25)
        {
            return false;
        }

        // PNG 签名 89 50 4E 47 0D 0A 1A 0A
        if (png[0] != 0x89 || png[1] != 0x50 || png[2] != 0x4E || png[3] != 0x47
            || png[4] != 0x0D || png[5] != 0x0A || png[6] != 0x1A || png[7] != 0x0A)
        {
            return false;
        }

        var bitDepth = 0;
        var colorType = -1;
        var interlace = 0;
        byte[]? palette = null;
        byte[]? paletteAlpha = null;
        var gotHeader = false;
        using var idat = new MemoryStream();

        var pos = 8;
        while (pos + 8 <= png.Length)
        {
            var len = ReadBe32(png, pos);
            if (len < 0 || len > png.Length || pos + 12 + len > png.Length)
            {
                return false;
            }
            var type = Encoding.ASCII.GetString(png, pos + 4, 4);
            var dataStart = pos + 8;

            if (type == "IHDR")
            {
                if (len < 13)
                {
                    return false;
                }
                width = ReadBe32(png, dataStart);
                height = ReadBe32(png, dataStart + 4);
                bitDepth = png[dataStart + 8];
                colorType = png[dataStart + 9];
                // 压缩方式 / 滤波方式必须为 0(PNG 规范)
                if (png[dataStart + 10] != 0 || png[dataStart + 11] != 0)
                {
                    return false;
                }
                interlace = png[dataStart + 12];
                gotHeader = true;
            }
            else if (type == "PLTE")
            {
                palette = png.AsSpan(dataStart, len).ToArray();
            }
            else if (type == "tRNS")
            {
                // 调色板 alpha(索引色 PNG 的透明通道);实测共鸣链白图多为 ct=3 索引色,
                // 不读该 chunk 会把透明背景误当不透明,导致整图被判成「黑」而漏检。
                paletteAlpha = png.AsSpan(dataStart, len).ToArray();
            }
            else if (type == "IDAT")
            {
                idat.Write(png, dataStart, len);
            }
            else if (type == "IEND")
            {
                break;
            }

            pos = dataStart + len + 4; // 跳过 CRC
        }

        if (!gotHeader || width <= 0 || height <= 0)
        {
            return false;
        }
        if (interlace != 0)
        {
            return false; // Adam7 隔行:不支持 → 不拦截
        }
        var channels = colorType switch
        {
            0 => 1, // 灰度
            2 => 3, // RGB
            3 => 1, // 调色板(每像素 1 个索引)
            4 => 2, // 灰度 + alpha
            6 => 4, // RGBA
            _ => 0,
        };
        if (channels == 0)
        {
            return false;
        }
        // 位深合法性(依 PNG 规范):索引色/灰度允许 1/2/4/8,其余 8/16
        var bitDepthOk = colorType switch
        {
            0 => bitDepth is 1 or 2 or 4 or 8 or 16,
            3 => bitDepth is 1 or 2 or 4 or 8,
            2 or 4 or 6 => bitDepth is 8 or 16,
            _ => false,
        };
        if (!bitDepthOk)
        {
            return false; // 异常位深 → 不拦截
        }
        if ((long)width * height > 4096L * 4096L)
        {
            return false; // 超大图不解析(图标不可能这么大)→ 不拦截
        }

        byte[] raw;
        try
        {
            idat.Position = 0;
            using var inflate = new ZLibStream(idat, CompressionMode.Decompress);
            using var inflated = new MemoryStream();
            inflate.CopyTo(inflated);
            raw = inflated.ToArray();
        }
        catch (Exception)
        {
            return false; // 数据损坏:不拦截
        }

        var stride = (width * channels * bitDepth + 7) / 8;
        var bytesPerPixel = Math.Max(1, channels * bitDepth / 8); // PNG 滤波的 bpp(不足 1 字节按 1 计)
        if (raw.LongLength < (long)(stride + 1) * height)
        {
            return false;
        }

        // 取第 x 像素第 c 通道的样本值(统一归一到 0..255 量级;16 位取高字节,判定纯白足够)
        int Sample(byte[] row, int x, int c)
        {
            if (bitDepth == 8)
            {
                return row[x * channels + c];
            }
            if (bitDepth == 16)
            {
                return row[(x * channels + c) * 2];
            }
            // 位深 < 8:仅灰度/调色板,每像素 1 个样本
            var bitIndex = x * bitDepth;
            var mask = (1 << bitDepth) - 1;
            var shift = 8 - bitDepth - (bitIndex & 7);
            var value = (row[bitIndex >> 3] >> shift) & mask;
            return colorType == 0 ? value * 255 / mask : value; // 灰度需按规范缩放到 0..255
        }

        rgba = new byte[width * height * 4];
        var prev = new byte[stride];
        var cur = new byte[stride];
        for (var y = 0; y < height; y++)
        {
            var rowStart = y * (stride + 1);
            var filter = raw[rowStart];
            if (filter > 4)
            {
                return false; // 未知滤波器:异常数据 → 不拦截
            }
            Buffer.BlockCopy(raw, rowStart + 1, cur, 0, stride);
            for (var x = 0; x < stride; x++)
            {
                int a = x >= bytesPerPixel ? cur[x - bytesPerPixel] : 0;
                int b = prev[x];
                int c = x >= bytesPerPixel ? prev[x - bytesPerPixel] : 0;
                cur[x] = filter switch
                {
                    0 => cur[x],
                    1 => (byte)(cur[x] + a),
                    2 => (byte)(cur[x] + b),
                    3 => (byte)(cur[x] + ((a + b) >> 1)),
                    _ => (byte)(cur[x] + Paeth(a, b, c)),
                };
            }

            for (var x = 0; x < width; x++)
            {
                var d = (y * width + x) * 4;
                switch (colorType)
                {
                    case 0:
                        rgba[d] = rgba[d + 1] = rgba[d + 2] = (byte)Sample(cur, x, 0);
                        rgba[d + 3] = 255;
                        break;
                    case 2:
                        rgba[d] = (byte)Sample(cur, x, 0);
                        rgba[d + 1] = (byte)Sample(cur, x, 1);
                        rgba[d + 2] = (byte)Sample(cur, x, 2);
                        rgba[d + 3] = 255;
                        break;
                    case 3:
                    {
                        var index = Sample(cur, x, 0);
                        if (palette is null || index * 3 + 2 >= palette.Length)
                        {
                            return false;
                        }
                        rgba[d] = palette[index * 3];
                        rgba[d + 1] = palette[index * 3 + 1];
                        rgba[d + 2] = palette[index * 3 + 2];
                        // 索引色 PNG 的透明由 tRNS 提供;缺省(无 tRNS)为不透明
                        rgba[d + 3] = paletteAlpha is not null && index < paletteAlpha.Length
                            ? paletteAlpha[index]
                            : (byte)255;
                        break;
                    }
                    case 4:
                        rgba[d] = rgba[d + 1] = rgba[d + 2] = (byte)Sample(cur, x, 0);
                        rgba[d + 3] = (byte)Sample(cur, x, 1);
                        break;
                    default: // 6: RGBA
                        rgba[d] = (byte)Sample(cur, x, 0);
                        rgba[d + 1] = (byte)Sample(cur, x, 1);
                        rgba[d + 2] = (byte)Sample(cur, x, 2);
                        rgba[d + 3] = (byte)Sample(cur, x, 3);
                        break;
                }
            }

            (prev, cur) = (cur, prev); // 已反滤波的当前行成为下一行的「上一行」
        }
        return true;
    }

    private static int ReadBe32(byte[] b, int offset)
        => (b[offset] << 24) | (b[offset + 1] << 16) | (b[offset + 2] << 8) | b[offset + 3];

    /// <summary>PNG Paeth 预测器(滤波类型 4)。</summary>
    private static int Paeth(int a, int b, int c)
    {
        var p = a + b - c;
        var pa = Math.Abs(p - a);
        var pb = Math.Abs(p - b);
        var pc = Math.Abs(p - c);
        if (pa <= pb && pa <= pc)
        {
            return a;
        }
        return pb <= pc ? b : c;
    }

    /// <summary>
    /// 一次性清理某分类已污染的磁盘缓存(删除本地 PNG + 索引条目),供调用方在确认后显式触发。
    /// <para>
    /// <b>不会自动调用</b>(不在启动时删除用户数据):修复共鸣链白图后,需要在用户确认下
    /// 调用 <c>PurgeCategory(CategoryChain)</c> 清掉历史遗留的纯白 PNG。
    /// 分类名非法(含路径分隔符/<c>..</c>)时直接返回 0,避免越权删除缓存目录以外的文件。
    /// </para>
    /// </summary>
    /// <returns>被清理的 PNG 文件数。</returns>
    public int PurgeCategory(string category)
    {
        if (!IsSafeCategory(category))
        {
            return 0;
        }

        var cachedDir = Path.GetFullPath(_cacheDir);
        var dir = Path.GetFullPath(Path.Combine(cachedDir, category));
        // 二次确认目标目录确实位于缓存目录之内(防目录穿越)
        if (!dir.StartsWith(cachedDir + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            return 0;
        }

        var removed = 0;
        try
        {
            if (Directory.Exists(dir))
            {
                foreach (var file in Directory.EnumerateFiles(dir, "*.png"))
                {
                    try
                    {
                        File.Delete(file);
                        removed++;
                    }
                    catch (Exception)
                    {
                        // 单文件删除失败(占用/权限)静默,继续清理其余文件
                    }
                }
            }
        }
        catch (Exception)
        {
            // 目录不可枚举:视为无可清理内容
        }

        lock (_lock)
        {
            _index.Remove(category);
        }
        SaveIndex();

        try
        {
            if (Directory.Exists(dir) && !Directory.EnumerateFileSystemEntries(dir).Any())
            {
                Directory.Delete(dir);
            }
        }
        catch (Exception)
        {
            // 空目录删除失败无害
        }

        return removed;
    }

    /// <summary>分类名是否可安全用作子目录名(拒绝空/分隔符/<c>..</c>)。</summary>
    private static bool IsSafeCategory(string category)
        => !string.IsNullOrWhiteSpace(category)
           && category.IndexOfAny(new[] { '\\', '/', ':' }) < 0
           && !category.Contains("..", StringComparison.Ordinal);

    /// <summary>把名称清洗为合法文件名(非法文件名字符替换为 '_')。</summary>
    public static string Safe(string name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return name ?? "";
        }

        var sb = new StringBuilder(name.Length);
        foreach (var c in name)
        {
            sb.Append(IsInvalidFileNameChar(c) ? '_' : c);
        }

        return sb.ToString();
    }

    private static bool IsInvalidFileNameChar(char c)
    {
        // Windows 非法文件名字符(\ / : * ? " < > |)与平台无关,统一替换;
        // 控制字符在任意平台都不可靠,一并替换。
        return c < 0x20 || c is '\\' or '/' or ':' or '*' or '?' or '"' or '<' or '>' or '|';
    }

    /// <summary>枚举角色详情所有 (category, name, url) 图标条目(跳过空名称/空 URL;chain 有意不枚举,见下)。</summary>
    private static IEnumerable<(string Category, string Name, string Url)> EnumerateIcons(RoleDetail role)
    {
        if (role.Role is { } r && !string.IsNullOrWhiteSpace(r.RolePicUrl))
        {
            yield return (CategoryRole, role.RoleName, r.RolePicUrl);
        }
        if (role.WeaponData?.Weapon is { } w
            && !string.IsNullOrWhiteSpace(w.WeaponIcon)
            && !string.IsNullOrWhiteSpace(role.WeaponData.DisplayName))
        {
            yield return (CategoryWeapon, role.WeaponData.DisplayName, w.WeaponIcon);
        }
        if (role.Skills is not null)
        {
            foreach (var s in role.Skills)
            {
                if (s.Skill is { } sk && !string.IsNullOrWhiteSpace(sk.SkillName) && !string.IsNullOrWhiteSpace(sk.IconUrl))
                {
                    yield return (CategorySkill, sk.SkillName, sk.IconUrl);
                }
            }
        }
        // 共鸣链(chain)在预下载阶段直接跳过,不枚举:chain 白线稿/白图污染问题未解决前不进磁盘缓存,
        // 而 ResolveIcon 对 chain 一律绕过本地缓存返回远端 URL —— 下载了也用不上,
        // 只会每次进角色详情页白费带宽,再被 Store 的白图防御拦下刷 warning(与绕过策略对齐)。
        if (role.Attributes is not null)
        {
            foreach (var a in role.Attributes)
            {
                if (!string.IsNullOrWhiteSpace(a.AttributeName) && !string.IsNullOrWhiteSpace(a.IconUrl))
                {
                    yield return (CategoryAttr, a.AttributeName, a.IconUrl);
                }
            }
        }
        if (role.PhantomData?.Phantoms is not null)
        {
            foreach (var e in role.PhantomData.Phantoms)
            {
                if (!string.IsNullOrWhiteSpace(e.PhantomName) && !string.IsNullOrWhiteSpace(e.IconUrl))
                {
                    yield return (CategoryEcho, e.PhantomName, e.IconUrl);
                }
            }
        }
    }

    private static bool IsHttpUrl(string url)
        => url.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
           || url.StartsWith("https://", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// 落盘 + 更新索引。两道防御:
    /// ① 分类名非法(<see cref="IsSafeCategory"/> 拒绝空/分隔符/<c>..</c>)直接拒绝 ——
    ///   公开写入口已在 <c>CacheOneAsync</c> 校验过,这里再拦一次,防止将来新增调用方绕过
    ///   而经 <c>Path.Combine</c> 写到缓存目录之外;
    /// ② 写入前用 <see cref="LooksLikeBlankOrWhite"/> 做防御:纯白/空图不入缓存
    ///   (实测共鸣链图标曾被污染为纯白 PNG),被拦截时只记 warning 并保留远端 URL,不影响渲染。
    /// </summary>
    /// <returns>true=已落盘;false=分类名非法或被白图防御拦截(均未写盘、未更新索引)。</returns>
    private bool Store(string category, string name, byte[] bytes)
    {
        if (!IsSafeCategory(category))
        {
            _logger.LogDebug("图标分类名非法,已拒绝落盘: category={Category} name={Name}", category, name);
            return false;
        }

        if (LooksLikeBlankOrWhite(bytes))
        {
            // 白图防御:不落盘(调用方会退回远端 URL,渲染不受影响)。仅记录,便于定位污染源。
            _logger.LogWarning(
                "图标疑似纯白/空图,已跳过磁盘缓存: category={Category} name={Name} size={Size}B",
                category, name, bytes.Length);
            return false;
        }

        var dir = Path.Combine(_cacheDir, category);
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, Safe(name) + ".png");
        // 原子落盘:先写一次性临时文件,再 File.Move(overwrite) 替换。
        // 直接 File.WriteAllBytes 若在写入中途崩溃/断电,会留下半截 PNG;而 GetCachedIconPath
        // 只做 File.Exists 校验,坏缓存会被永久命中(每次进详情页都显示不出图标)。
        var tmp = path + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            File.WriteAllBytes(tmp, bytes);
            File.Move(tmp, path, overwrite: true);
        }
        catch (Exception)
        {
            try
            {
                if (File.Exists(tmp))
                {
                    File.Delete(tmp); // 失败不留临时文件(PurgeCategory 只清 *.png)
                }
            }
            catch (Exception)
            {
                // 临时文件清理失败无害
            }
            throw; // 交给 DownloadAndStoreAsync 静默:索引不更新,下次进详情页会重试
        }
        lock (_lock)
        {
            if (!_index.TryGetValue(category, out var map))
            {
                map = new Dictionary<string, string>(StringComparer.Ordinal);
                _index[category] = map;
            }
            map[name] = path;
        }
        return true;
    }

    private void LoadIndex()
    {
        try
        {
            if (!File.Exists(_indexPath))
            {
                return;
            }
            var json = File.ReadAllText(_indexPath);
            if (string.IsNullOrWhiteSpace(json))
            {
                return;
            }
            var loaded = JsonSerializer.Deserialize(json, IconCacheJsonContext.Default.IconCacheIndex);
            if (loaded?.Categories is not { } categories)
            {
                return;
            }
            lock (_lock)
            {
                _index.Clear();
                foreach (var kv in categories)
                {
                    _index[kv.Key] = new Dictionary<string, string>(kv.Value, StringComparer.Ordinal);
                }
            }
        }
        catch (Exception)
        {
            // 索引损坏/不可读:从空开始,下次缓存重建
        }
    }

    /// <summary>把当前索引持久化到 index.json(锁内串行写,失败静默)。</summary>
    public void SaveIndex()
    {
        lock (_lock)
        {
            try
            {
                Directory.CreateDirectory(_cacheDir);
                var doc = new IconCacheIndex { Categories = _index };
                var json = JsonSerializer.Serialize(doc, IconCacheJsonContext.Default.IconCacheIndex);
                File.WriteAllText(_indexPath, json);
            }
            catch (Exception)
            {
                // 写索引失败静默(磁盘只读等),不影响已落盘的图标文件
            }
        }
    }
}

/// <summary>索引文件 JSON 结构:categories = category → name → 本地文件路径。</summary>
public sealed class IconCacheIndex
{
    public Dictionary<string, Dictionary<string, string>> Categories { get; set; } = new(StringComparer.Ordinal);
}

[JsonSerializable(typeof(IconCacheIndex))]
internal sealed partial class IconCacheJsonContext : JsonSerializerContext;
