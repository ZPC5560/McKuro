using System.Buffers;
using System.Security.Cryptography;
using McKuro.Core.Models.Game;

namespace McKuro.Core.Services.Game;

/// <summary>
/// 快速校验(Haiyu 1.4.3 的「快速校验」开关):对带分片信息的文件,
/// 只校验「文件大小 + 若干采样分片」,跳过其余分片的整段读取。
/// <para>
/// 动机:全量校验要对每个文件整份读盘算 MD5,数万文件的游戏目录是分钟级瓶颈;
/// 而分片清单里的分片哈希同样来自服务器,采样若干片即可较高置信地判断文件是否被改动/截断。
/// </para>
/// <para>
/// <b>⚠ 已知取舍(必须如实告知,不要宣传成"等价于全量校验"):</b>
/// 本方法<b>只证明被采样的那些分片完好</b>。<b>未被采样分片里的损坏不会被发现</b> ——
/// 长度对、采样片对、其余片坏,本方法返回 <c>true</c>(判为完好),而全文件 MD5 会判为损坏。
/// 因此本开关<b>默认关闭</b>,且仅在「更新」路径生效;「修复游戏」路径强制全量校验
/// (用户点修复的前提就是"文件可能坏了")。任何采样分片缺少哈希、或文件没有分片信息时,
/// 返回 <c>false</c> 交由调用方回退全文件 MD5。
/// </para>
/// <para>
/// <b>采样数是按分片数成比例放大的(而非固定 3 片)</b> —— 这是实测后修正的设计,原因见
/// <see cref="SampleCountFor"/>:官方分片约 100 MB/片,固定 3 片会让「文件越大覆盖率越低」,
/// 18.6 GB/186 片的 pak 只能查到 1.5%,恰好把最该查的大文件放过去了。
/// </para>
/// <para>
/// 实现为同步:调用方 <see cref="UpdateInstaller.ComputeDiff"/> 本身用
/// <see cref="Parallel.ForEach{T}(IEnumerable{T}, Action{T})"/> 并行读盘,
/// 同步读避免在并行循环里再叠加异步状态机与线程池调度开销。
/// </para>
/// </summary>
public static class FastVerify
{
    /// <summary>默认采样分片数(小文件:首、中、尾)。</summary>
    public const int DefaultSampleCount = 3;

    /// <summary>分片数不超过此值时<b>全部分片都校验</b>(此时采样等于全量,覆盖率 100%)。</summary>
    public const int FullCoverageChunkLimit = 8;

    /// <summary>大文件的采样分片数上限(避免超大文件退化成近全量读盘)。</summary>
    public const int MaxSampleCount = 64;

    /// <summary>读取缓冲区大小(128 KiB)。</summary>
    private const int BufferSize = 128 * 1024;

    /// <summary>
    /// 该文件应采样多少个分片(纯函数,便于单测)。
    /// <para>
    /// 规则:<c>n &lt;= 8</c> → 全查 n 片(覆盖率 100%);否则 → <c>ceil(n/2)</c>,上限 64。
    /// </para>
    /// <para>
    /// <b>为什么不能固定 3 片(实测依据,鸣潮 CN 3.7.0 官方校验索引,76 个带分片文件 / 78.5 GB):</b>
    /// 官方分片约 100 MB 一片,所以固定 3 片的效果是<b>反的</b> ——
    /// 2 片的小文件覆盖 100%(白读全份,零收益),而 186 片的 18.6 GB pak 只覆盖 <b>1.5%</b>
    /// (18.3 GB 不校验)。整体"只读 20.7%"的漂亮数字,主要来自放过了最该查的大文件;
    /// 而未验证字节高达 79.3%。按本规则:读盘 47.2 GB(1.7× 提速)、未验证降到 <b>39.9%</b>,
    /// 大文件覆盖率从 1.5% 提到 <b>34.3%</b>,且没有任何文件的覆盖率劣于固定 3 片。
    /// </para>
    /// </summary>
    public static int SampleCountFor(int chunkCount)
    {
        if (chunkCount <= 0)
        {
            return 0;
        }

        if (chunkCount <= FullCoverageChunkLimit)
        {
            return chunkCount; // 小文件:全查,覆盖率 100%
        }

        return Math.Min(MaxSampleCount, (chunkCount + 1) / 2);
    }

    /// <summary>
    /// 选取要采样的分片(纯函数,便于单测):在按 <see cref="GameChunkInfo.Start"/> 升序排列后
    /// 均匀取 <paramref name="sampleCount"/> 个位置(必含首尾),按其顺序返回。
    /// 分片数不超过采样数时返回全部分片。
    /// </summary>
    /// <param name="chunks">分片列表(顺序不限)。</param>
    /// <param name="sampleCount">采样数量,默认 3。</param>
    public static IReadOnlyList<GameChunkInfo> SelectSampledChunks(
        IReadOnlyList<GameChunkInfo> chunks,
        int sampleCount = DefaultSampleCount)
    {
        if (chunks.Count == 0)
        {
            return [];
        }

        var ordered = chunks.OrderBy(c => c.Start).ToList();
        if (sampleCount <= 1 || ordered.Count <= sampleCount)
        {
            return ordered;
        }

        var picked = new List<GameChunkInfo>(sampleCount);
        for (var i = 0; i < sampleCount; i++)
        {
            // 末位对齐到最后一索引,避免整数除法把最后一个采样点提前。
            var index = (int)((long)i * (ordered.Count - 1) / (sampleCount - 1));
            var chunk = ordered[index];
            if (!picked.Contains(chunk))
            {
                picked.Add(chunk);
            }
        }

        return picked;
    }

    /// <summary>
    /// 该条目是否「可以」走快速校验:声明了<b>正数</b>文件大小、有分片信息,且所有采样分片都带哈希。
    /// <para>
    /// 供调用方区分「快速校验判定为损坏」与「根本无法快速判定」——
    /// 后者必须回退全文件校验,否则会把完好文件误判成需要下载。
    /// </para>
    /// <para>
    /// <b>为什么必须要求 <c>Size &gt; 0</c></b>:长度校验是快速判定的前提之一。
    /// 清单里 <c>size</c> 缺失时会被解析成 0(<c>KuroFileInfo.Size</c> 是 <c>long?</c>,
    /// 加载器写的是 <c>file.Size ?? 0</c>),此时长度校验退化失效 ——
    /// 若仍走快速路径,一个「内容与清单完全不符」的文件只要采样分片对上就会被判为完好并被跳过
    /// (而全文件 MD5 本可以发现)。大小未知时一律交回全文件 MD5 判定。
    /// </para>
    /// </summary>
    /// <param name="sampleCount">
    /// 采样分片数;<c>null</c>(默认)= 按 <see cref="SampleCountFor"/> 依分片数自动放大。
    /// </param>
    public static bool CanFastVerify(GameFileEntry entry, int? sampleCount = null)
    {
        if (entry.Size <= 0 || entry.ChunkInfos.Count == 0)
        {
            return false;
        }

        var sampled = SelectSampledChunks(entry.ChunkInfos, sampleCount ?? SampleCountFor(entry.ChunkInfos.Count));
        return sampled.Count > 0 && sampled.All(c => !string.IsNullOrWhiteSpace(c.Md5));
    }

    /// <summary>
    /// 分片是否<b>完整且连续地覆盖整个文件</b>(从 0 开始、首尾相接、末端正好等于
    /// <see cref="GameFileEntry.Size"/>,且每片都带哈希)。
    /// <para>
    /// <b>用途:消除「刚下完又整份复算一遍 MD5」的重复读盘。</b>
    /// 分片下载路径对<b>每一个</b>分片都做过哈希复核(命中即跳过、写完再复核),
    /// 因此当分片完整覆盖时,文件内容已经被逐字节验证过,再算一遍整文件 MD5 是纯粹重复读盘。
    /// </para>
    /// <para>
    /// <b>实测依据(鸣潮 CN 3.7.1 主清单):</b>带分片的条目共 <b>76 个 / 78.54 GB</b>,
    /// 且 <b>76 个全部</b>同时带整文件 md5、<b>全部</b>满足本判定。
    /// 也就是说每次安装都有 78.54 GB 被多读一遍 —— 按 577 MiB/s 计约合两分多钟纯等待。
    /// </para>
    /// <para>
    /// <b>这个函数只回答「能不能跳过」,不回答「文件是否正确」</b>;调用方在返回 true 时
    /// 仍必须校验最终文件长度(分片覆盖已含末端,但 SetLength 之类的截断仍需兜一次)。
    /// 分片不完整(有空洞/末端不足/<see cref="GameFileEntry.Size"/> 未知)时一律返回 false,
    /// 交回整文件 MD5 —— 宁可多读一遍,也不能放过未验证的字节。
    /// </para>
    /// </summary>
    public static bool HasCompleteChunkCoverage(GameFileEntry entry)
    {
        if (entry.Size <= 0 || entry.ChunkInfos.Count == 0)
        {
            return false;
        }

        long next = 0;
        foreach (var chunk in entry.ChunkInfos.OrderBy(c => c.Start))
        {
            // 必须首尾相接(起点正好是上一个的终点 +1)、区间合法、且带哈希。
            if (chunk.Start != next
                || chunk.End < chunk.Start
                || chunk.End >= entry.Size
                || string.IsNullOrWhiteSpace(chunk.Md5))
            {
                return false;
            }

            next = chunk.End + 1;
        }

        return next == entry.Size;
    }

    /// <summary>
    /// 用分片哈希校验<b>整个文件</b>(要求 <see cref="HasCompleteChunkCoverage"/> 为 true)。
    /// <para>
    /// <b>为什么校验侧也要走这里(而不是直接算整文件 MD5):</b>
    /// 下载侧在分片完整覆盖时<b>不再</b>算整文件 MD5(见 <see cref="FileDownloader"/> 的分片收尾),
    /// 若校验侧仍用整文件 MD5,两侧就用了<b>不同判据</b> —— 一旦清单里的整文件 md5 与分片哈希
    /// 自相矛盾,就会出现「校验说无需下载、下载器却拒绝跳过」的反复横跳。
    /// 本方法让两侧对同一个文件用<b>同一份证据</b>,结论必然一致。
    /// </para>
    /// <para>
    /// <b>性能上不亏:</b>完整覆盖时分片校验本身就要读完整个文件,与整文件 MD5 的读盘量相同
    /// (都是顺序一遍),因此这是一次纯粹的「一致性」改进,不是折衷。
    /// 需要<b>少读</b>时应改用采样式的 <see cref="VerifyFileFast"/>(那是另一个开关与取舍)。
    /// </para>
    /// </summary>
    /// <returns>文件完好时 true;覆盖判定不成立、文件缺失或任一分片不符时 false。</returns>
    public static bool VerifyAllChunks(string path, GameFileEntry entry, CancellationToken ct = default)
    {
        if (!HasCompleteChunkCoverage(entry) || !File.Exists(path))
        {
            return false;
        }

        try
        {
            using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                BufferSize,
                FileOptions.SequentialScan);
            if (stream.Length != entry.Size)
            {
                return false;
            }

            // 覆盖判定已保证首尾相接,按序校验即可。
            foreach (var chunk in entry.ChunkInfos.OrderBy(c => c.Start))
            {
                ct.ThrowIfCancellationRequested();
                if (!ChunkHashMatches(stream, chunk, ct))
                {
                    return false;
                }
            }

            return true;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            // 读盘失败(被占用/无权限)按"需要下载"处理,与 VerifyFileFast 的处置一致。
            return false;
        }
    }

    /// <summary>
    /// 快速判定本地文件是否与清单一致:文件存在、长度<b>精确等于</b>
    /// <see cref="GameFileEntry.Size"/>,且所有采样分片哈希一致。
    /// <para>
    /// 调用前应先用 <see cref="CanFastVerify"/> 确认可快速判定;本方法自身也做守卫:
    /// <c>Size &lt;= 0</c>(大小未知)时直接返回 <c>false</c>,避免长度校验退化失效后
    /// 把「内容与清单不符」的文件误判为完好。注意这里<b>不做</b>
    /// <c>Size &gt; 0</c> 的宽松处理 —— 与 <see cref="FileDownloader.VerifyLocalFile"/> 不同,
    /// 本方法没有文件级 MD5 兜底,长度是唯一的内容完整性前提。
    /// </para>
    /// </summary>
    /// <returns>true = 文件完好(可跳过下载);false = 需重新下载,或无法快速判定(回退全文件校验)。</returns>
    /// <param name="sampleCount">
    /// 采样分片数;<c>null</c>(默认)= 按 <see cref="SampleCountFor"/> 依分片数自动放大。
    /// </param>
    public static bool VerifyFileFast(
        string path,
        GameFileEntry entry,
        int? sampleCount = null,
        CancellationToken ct = default)
    {
        if (!File.Exists(path))
        {
            return false;
        }

        // 大小未知(清单缺 size → 0)时不具备判定前提,交回全文件 MD5。
        if (entry.Size <= 0)
        {
            return false;
        }

        // 长度必须精确匹配:仅凭分片采样不能证明文件完整。
        if (new FileInfo(path).Length != entry.Size)
        {
            return false;
        }

        var sampled = SelectSampledChunks(
            entry.ChunkInfos,
            sampleCount ?? SampleCountFor(entry.ChunkInfos.Count));
        if (sampled.Count == 0)
        {
            return false;
        }

        // 任一采样分片没有哈希就无法校验 —— 交回全文件校验,不猜"大概没问题"。
        if (sampled.Any(c => string.IsNullOrWhiteSpace(c.Md5)))
        {
            return false;
        }

        try
        {
            using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: BufferSize,
                useAsync: false);

            foreach (var chunk in sampled)
            {
                ct.ThrowIfCancellationRequested();
                if (!ChunkHashMatches(stream, chunk, ct))
                {
                    return false;
                }
            }

            return true;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            // 读盘失败(占用/权限)按"需要下载"处理,后续下载流程会再给出具体错误。
            return false;
        }
    }

    /// <summary>
    /// 校验文件中某个分片范围的 MD5 是否与分片清单一致。
    /// 分片路径的下载后复核与快速校验共用这一份实现。
    /// </summary>
    /// <returns>分片无哈希、文件短于分片末尾、或哈希不符时返回 false。</returns>
    public static bool ChunkHashMatches(FileStream file, GameChunkInfo chunk, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(chunk.Md5) || file.Length < chunk.End + 1)
        {
            return false;
        }

        file.Position = chunk.Start;
        using var md5 = IncrementalHash.CreateHash(HashAlgorithmName.MD5);
        // 从 ArrayPool 租借而非 new byte[]:本方法按分片调用,快速校验又是 8 路并行,
        // 每个分片都新分配 128 KiB 属于纯粹的无谓分配(大文件百余个分片时可达数十 MB)。
        var buffer = ArrayPool<byte>.Shared.Rent(BufferSize);
        try
        {
            var remaining = chunk.Length;
            while (remaining > 0)
            {
                ct.ThrowIfCancellationRequested();
                var read = file.Read(buffer, 0, (int)Math.Min(BufferSize, remaining));
                if (read == 0)
                {
                    return false;
                }

                md5.AppendData(buffer, 0, read);
                remaining -= read;
            }

            return string.Equals(
                Convert.ToHexStringLower(md5.GetHashAndReset()),
                chunk.Md5,
                StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }
}
