using System.Security.Cryptography;
using System.Text;
using McKuro.Core.Models.Game;
using McKuro.Core.Services.Game;

namespace McKuro.Tests;

/// <summary>
/// 快速校验(移植自 Haiyu 1.4.3)的判定测试。
/// <para>
/// 核心契约:快速校验只能「更早发现损坏」,绝不能放过损坏文件;
/// 无法快速判定时必须回退全文件 MD5,而不是乐观地认为文件完好。
/// </para>
/// </summary>
public sealed class FastVerifyTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "McKuro_fastverify_" + Guid.NewGuid().ToString("N"));

    public FastVerifyTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch
        {
            // 忽略清理失败
        }
    }

    // ---------- 采样选择(纯函数) ----------

    [Fact]
    public void 采样选择_取首中尾三个分片()
    {
        var chunks = Chunks(5, chunkSize: 8, data: new byte[40]);
        var sampled = FastVerify.SelectSampledChunks(chunks);
        Assert.Equal(3, sampled.Count);
        Assert.Equal([0L, 16L, 32L], sampled.Select(c => c.Start).ToArray());
    }

    [Fact]
    public void 采样选择_分片数不足时返回全部()
    {
        var chunks = Chunks(2, chunkSize: 8, data: new byte[16]);
        var sampled = FastVerify.SelectSampledChunks(chunks);
        Assert.Equal(2, sampled.Count);
        Assert.Equal([0L, 8L], sampled.Select(c => c.Start).ToArray());
    }

    [Fact]
    public void 采样选择_空分片返回空()
    {
        Assert.Empty(FastVerify.SelectSampledChunks([]));
    }

    [Fact]
    public void 采样选择_单分片不会重复()
    {
        var chunks = Chunks(1, chunkSize: 8, data: new byte[8]);
        var sampled = FastVerify.SelectSampledChunks(chunks);
        Assert.Single(sampled);
    }

    [Fact]
    public void 采样选择_乱序输入按起点升序处理()
    {
        var chunks = Chunks(4, chunkSize: 8, data: new byte[32]);
        chunks.Reverse();
        var sampled = FastVerify.SelectSampledChunks(chunks);
        Assert.Equal(sampled.OrderBy(c => c.Start).Select(c => c.Start), sampled.Select(c => c.Start));
    }

    // ---------- CanFastVerify:区分「损坏」与「无法快速判定」 ----------

    /// <summary>
    /// <b>独立评审发现的真实缺陷(已修)</b>:清单里 <c>size</c> 缺失时会被解析成 0
    /// (<c>KuroIndexModels.KuroFileInfo.Size</c> 是 <c>long?</c>,加载器写 <c>file.Size ?? 0</c>)。
    /// 此时若仍走快速路径,原来的 <c>entry.Size &gt; 0 &amp;&amp; ...</c> 守卫会让长度校验<b>退化失效</b> ——
    /// 一个「内容与清单完全不符」的文件只要采样分片对上就被判为完好并被跳过,
    /// 而全文件 MD5 本可以发现它。
    /// </summary>
    [Fact]
    public void 大小为0时不得走快速校验_内容不符的文件必须被发现()
    {
        var data = Encoding.UTF8.GetBytes("0123456789abcdef"); // 16 字节
        var path = WriteFile("size0.bin", data);

        // 清单声明 Size=0(缺 size 字段)、且文件级 MD5 指向"另一份内容"
        var entry = new GameFileEntry
        {
            Path = "size0.bin",
            Size = 0,
            Md5 = new string('f', 32),
        };
        entry.ChunkInfos.AddRange(Chunks(2, chunkSize: 8, data: data)); // 采样分片哈希与磁盘内容一致

        // 不能声称"可快速校验"(大小未知 ⇒ 不具备判定前提)
        Assert.False(FastVerify.CanFastVerify(entry), "Size==0 时必须拒绝快速校验");
        Assert.False(FastVerify.VerifyFileFast(path, entry), "Size==0 时 VerifyFileFast 必须返回 false");

        // 关键:整体判定必须与「关闭开关」一致 —— 内容与清单不符 → 需要下载
        Assert.False(
            UpdateInstaller.IsLocalFileIntact(path, entry, fastVerify: true),
            "Size==0 时不得因采样分片对上就判为完好,否则会漏过一个内容完全不符的文件");
        Assert.False(UpdateInstaller.IsLocalFileIntact(path, entry, fastVerify: false));
    }

    /// <summary>Size==0 且确实为空文件:仍不得走快速路径(无从校验),但与旧行为保持一致地判为需要下载。</summary>
    [Fact]
    public void 大小为0且文件为空时行为与关闭开关一致()
    {
        var path = WriteFile("empty.bin", []);
        var entry = new GameFileEntry { Path = "empty.bin", Size = 0, Md5 = "" };
        entry.ChunkInfos.AddRange(Chunks(1, chunkSize: 8, data: new byte[8]));

        // 开关开/关结论必须一致(快速路径不参与)
        Assert.Equal(
            UpdateInstaller.IsLocalFileIntact(path, entry, fastVerify: false),
            UpdateInstaller.IsLocalFileIntact(path, entry, fastVerify: true));
    }

    [Fact]
    public void 可快速校验_需要分片且采样分片带哈希()
    {
        var entry = Entry("a.bin", 32);
        Assert.False(FastVerify.CanFastVerify(entry));

        entry.ChunkInfos.AddRange(Chunks(4, chunkSize: 8, data: new byte[32]));
        Assert.True(FastVerify.CanFastVerify(entry));

        // 采样分片(首/尾)缺哈希 → 不能快速校验
        entry.ChunkInfos = WithoutHash(entry.ChunkInfos, 0);
        Assert.False(FastVerify.CanFastVerify(entry));
    }

    // ---------- VerifyFileFast ----------

    [Fact]
    public void 快速校验_完好文件通过()
    {
        var data = Encoding.UTF8.GetBytes("0123456789abcdefghijklmnopqrstuv");
        var path = WriteFile("ok.bin", data);
        var entry = Entry("ok.bin", data.Length);
        entry.ChunkInfos.AddRange(Chunks(4, chunkSize: 8, data: data));

        Assert.True(FastVerify.VerifyFileFast(path, entry));
    }

    [Fact]
    public void 快速校验_长度不符判为需要下载()
    {
        var data = Encoding.UTF8.GetBytes("0123456789abcdefghijklmnopqrstuv");
        var path = WriteFile("short.bin", data[..16]);
        var entry = Entry("short.bin", data.Length);
        entry.ChunkInfos.AddRange(Chunks(4, chunkSize: 8, data: data));

        Assert.False(FastVerify.VerifyFileFast(path, entry));
    }

    [Fact]
    public void 快速校验_采样分片损坏判为需要下载()
    {
        var data = Encoding.UTF8.GetBytes("0123456789abcdefghijklmnopqrstuv");
        var damaged = (byte[])data.Clone();
        damaged[0] = (byte)'X'; // 首片损坏
        var path = WriteFile("damaged.bin", damaged);
        var entry = Entry("damaged.bin", data.Length);
        entry.ChunkInfos.AddRange(Chunks(4, chunkSize: 8, data: data));

        Assert.False(FastVerify.VerifyFileFast(path, entry));
    }

    [Fact]
    public void 快速校验_采样分片缺哈希时不乐观通过()
    {
        var data = Encoding.UTF8.GetBytes("0123456789abcdefghijklmnopqrstuv");
        var path = WriteFile("nohash.bin", data);
        var entry = Entry("nohash.bin", data.Length);
        entry.ChunkInfos.AddRange(Chunks(4, chunkSize: 8, data: data));
        entry.ChunkInfos = WithoutHash(entry.ChunkInfos, 0);

        Assert.False(FastVerify.VerifyFileFast(path, entry));
    }

    [Fact]
    public void 快速校验_文件不存在返回false()
    {
        var entry = Entry("missing.bin", 32);
        entry.ChunkInfos.AddRange(Chunks(4, chunkSize: 8, data: new byte[32]));
        Assert.False(FastVerify.VerifyFileFast(Path.Combine(_root, "missing.bin"), entry));
    }

    // ---------- IsLocalFileIntact:开关语义 ----------

    [Fact]
    public void 关闭快速校验_未采样分片损坏也能发现()
    {
        var data = Encoding.UTF8.GetBytes("0123456789abcdefghijklmnopqrstuv");
        var damaged = (byte[])data.Clone();
        damaged[8] = (byte)'X'; // 第 2 片(不在采样集合里)损坏
        var path = WriteFile("mid.bin", damaged);
        var entry = Entry("mid.bin", data.Length, data); // 带正确文件级 MD5
        entry.ChunkInfos.AddRange(Chunks(4, chunkSize: 8, data: data));

        // 关闭开关 → 全文件 MD5,必须发现损坏
        Assert.False(UpdateInstaller.IsLocalFileIntact(path, entry, fastVerify: false));
    }

    [Fact]
    public void 开启快速校验_完好文件不误判为需要下载()
    {
        var data = Encoding.UTF8.GetBytes("0123456789abcdefghijklmnopqrstuv");
        var path = WriteFile("intact.bin", data);
        var entry = Entry("intact.bin", data.Length);
        entry.ChunkInfos.AddRange(Chunks(4, chunkSize: 8, data: data));

        Assert.True(UpdateInstaller.IsLocalFileIntact(path, entry, fastVerify: true));
    }

    [Fact]
    public void 开启快速校验_无分片信息时回退全文件校验()
    {
        var data = Encoding.UTF8.GetBytes("0123456789abcdefghijklmnopqrstuv");
        var path = WriteFile("nochunks.bin", data);
        var entry = Entry("nochunks.bin", data.Length, data); // 无 ChunkInfos

        Assert.True(UpdateInstaller.IsLocalFileIntact(path, entry, fastVerify: true));

        // 内容被改坏(长度不变)→ 必须被发现
        var damaged = (byte[])data.Clone();
        damaged[5] = (byte)'X';
        var damagedPath = WriteFile("nochunks_bad.bin", damaged);
        var damagedEntry = Entry("nochunks_bad.bin", data.Length, data);
        Assert.False(UpdateInstaller.IsLocalFileIntact(damagedPath, damagedEntry, fastVerify: true));
    }

    /// <summary>
    /// 关键边界:有分片信息但采样分片缺哈希时,快速校验无法判定 ——
    /// 必须回退全文件 MD5,否则会把完好的大文件误判成需要下载(数 GB 的无谓重下)。
    /// </summary>
    [Fact]
    public void 开启快速校验_采样分片缺哈希时回退全文件校验而非误判()
    {
        var data = Encoding.UTF8.GetBytes("0123456789abcdefghijklmnopqrstuv");
        var path = WriteFile("fallback.bin", data);
        var entry = Entry("fallback.bin", data.Length, data);
        entry.ChunkInfos.AddRange(Chunks(4, chunkSize: 8, data: data));
        entry.ChunkInfos = WithoutHash(entry.ChunkInfos, 0); // 采样分片缺哈希 → 无法快速判定

        // 文件其实是好的:回退全文件 MD5 后应判为完好(而不是误判需要下载)
        Assert.True(
            UpdateInstaller.IsLocalFileIntact(path, entry, fastVerify: true),
            "采样分片缺哈希时应回退全文件校验,而不是把完好文件判成需要下载");
    }

    /// <summary>
    /// 采样分片缺哈希 + 文件确实损坏 → 回退全文件 MD5 仍必须发现损坏。
    /// </summary>
    [Fact]
    public void 开启快速校验_采样分片缺哈希且文件损坏仍能发现()
    {
        var data = Encoding.UTF8.GetBytes("0123456789abcdefghijklmnopqrstuv");
        var damaged = (byte[])data.Clone();
        damaged[11] = (byte)'X';
        var path = WriteFile("fallback_bad.bin", damaged);
        var entry = Entry("fallback_bad.bin", data.Length, data); // 文件级 MD5 是"正确内容"的
        entry.ChunkInfos.AddRange(Chunks(4, chunkSize: 8, data: data));
        entry.ChunkInfos = WithoutHash(entry.ChunkInfos, 0);

        Assert.False(UpdateInstaller.IsLocalFileIntact(path, entry, fastVerify: true));
    }

    /// <summary>
    /// 回退语义:<b>有 size 但无 MD5</b> 时,长度匹配即视为完好(无从校验内容)。
    /// 这一宽松处理必须保留,否则会让一批无 MD5 的大文件被无谓重下。
    /// 注意边界:<b>size 与 md5 都空</b>时不再宽松(见下一条)。
    /// </summary>
    [Fact]
    public void 开启快速校验_无MD5但有size时与未开启行为一致()
    {
        var data = Encoding.UTF8.GetBytes("0123456789abcdefghijklmnopqrstuv");
        var path = WriteFile("nomd5.bin", data);
        var entry = Entry("nomd5.bin", data.Length); // Md5 为空,但 Size > 0

        Assert.True(UpdateInstaller.IsLocalFileIntact(path, entry, fastVerify: false));
        Assert.True(UpdateInstaller.IsLocalFileIntact(path, entry, fastVerify: true));
    }

    /// <summary>
    /// <b>两侧一致性不变量(评审发现的既有不一致,已修)</b>:校验侧
    /// (<see cref="UpdateInstaller.IsLocalFileIntact"/>)与下载侧
    /// (<see cref="FileDownloader.LocalFileMatches"/>)必须共用同一判据。
    /// <para>
    /// 曾经的缺陷:一条 <c>size=0 且 md5=""</c> 的清单行会被校验侧判为「完好、无需下载」,
    /// 而下载侧拒绝跳过 —— 用户看到「0 个文件待下载」却始终不动。
    /// 真实清单无此行(鸣潮 0 条;战双 2 条 size=0 均带 md5),属潜在问题。
    /// </para>
    /// <para>
    /// 本测试对「有无依据 × 长度是否匹配 × md5 是否正确」的组合逐一断言两侧结论相同,
    /// 将来任一侧被单独修改都会立刻失败。
    /// </para>
    /// </summary>
    [Fact]
    public void 不变量_校验侧与下载侧对同一清单行必须给出一致结论()
    {
        var data = Encoding.UTF8.GetBytes("0123456789abcdefghijklmnopqrstuv");
        var correct = Convert.ToHexStringLower(System.Security.Cryptography.MD5.HashData(data));
        var wrong = new string('f', 32);
        var onDisk = WriteFile("both.bin", data);
        var missing = Path.Combine(_root, "absent.bin");

        var cases = new (string Name, string Path, long Size, string Md5)[]
        {
            ("size+md5 正确", onDisk, data.Length, correct),
            ("size+md5 错误", onDisk, data.Length, wrong),
            ("size 正确、md5 空", onDisk, data.Length, ""),
            ("size 错误、md5 空", onDisk, data.Length + 1, ""),
            ("size 与 md5 皆空(无依据)", onDisk, 0, ""),
            ("size 与 md5 皆空 + 文件不存在", missing, 0, ""),
            ("size 正确、md5 空 + 文件不存在", missing, data.Length, ""),
        };

        foreach (var (name, path, size, md5) in cases)
        {
            var entry = new GameFileEntry { Path = "both.bin", Size = size, Md5 = md5 };
            foreach (var fast in new[] { false, true })
            {
                var verifySide = UpdateInstaller.IsLocalFileIntact(path, entry, fastVerify: fast);
                var downloadSide = FileDownloader.LocalFileMatches(path, entry);
                Assert.True(
                    verifySide == downloadSide,
                    $"两侧结论不一致[{name}, fastVerify={fast}]: 校验侧={verifySide}, 下载侧={downloadSide}");
            }
        }

        // 关键回归点:无任何完整性依据时,两侧都必须判为「不OK」(宁可重下,不谎报成功)
        var noEvidence = new GameFileEntry { Path = "both.bin", Size = 0, Md5 = "" };
        Assert.False(FileDownloader.LocalFileMatches(onDisk, noEvidence));
        Assert.False(UpdateInstaller.IsLocalFileIntact(onDisk, noEvidence, fastVerify: true));
        Assert.False(UpdateInstaller.IsLocalFileIntact(onDisk, noEvidence, fastVerify: false));
    }

    /// <summary>
    /// <b>新规则(实测后修正):分片数 ≤ <see cref="FastVerify.FullCoverageChunkLimit"/> 时全部分片都校验</b>,
    /// 覆盖率 100%,因此<b>任意分片损坏都能被发现</b> —— 不再有"未采样分片漏判"的缺口。
    /// <para>
    /// 旧规则固定 3 片,5 片文件只查 0/2/4,第 2 片损坏会被漏过(评审发现)。修正的起因是实测:
    /// 官方分片约 100 MB/片,固定 3 片会让小文件白读全份(零收益)、大文件覆盖率跌到 1.5%(放过最该查的)。
    /// </para>
    /// </summary>
    [Fact]
    public void 小文件全分片校验_任意分片损坏都能发现()
    {
        var data = Encoding.UTF8.GetBytes("0123456789abcdefghijklmnopqrstuvwxyzABCDEFGHIJ"); // 40 字节 = 5×8
        var damaged = (byte[])data.Clone();
        damaged[8] = (byte)'X'; // 第 2 个分片(旧规则下不被采样,现在必须被查到)
        var path = WriteFile("unsampled.bin", damaged);
        var entry = Entry("unsampled.bin", data.Length, data);
        entry.ChunkInfos.AddRange(Chunks(5, chunkSize: 8, data: data));

        Assert.Equal(5, FastVerify.SampleCountFor(5)); // 5 ≤ 8 → 全查
        Assert.Equal(5, FastVerify.SelectSampledChunks(entry.ChunkInfos, FastVerify.SampleCountFor(5)).Count);
        Assert.False(
            UpdateInstaller.IsLocalFileIntact(path, entry, fastVerify: true),
            "分片数 ≤ 8 时必须全分片校验,任意分片损坏都要能发现");
        Assert.False(UpdateInstaller.IsLocalFileIntact(path, entry, fastVerify: false));
    }

    /// <summary>
    /// <b>采样数按分片数成比例放大,不再固定 3 片。</b>
    /// 锁住"覆盖率不随文件变大而崩塌"这一修正后的性质。
    /// </summary>
    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(8)]
    [InlineData(9)]
    [InlineData(16)]
    [InlineData(68)]
    [InlineData(186)]
    [InlineData(1000)]
    public void 采样数随分片数放大_覆盖率不再随文件变大而崩塌(int chunkCount)
    {
        var k = FastVerify.SampleCountFor(chunkCount);
        var sampled = FastVerify.SelectSampledChunks(
            Chunks(chunkCount, chunkSize: 8, data: new byte[chunkCount * 8]), k);

        Assert.Equal(Math.Min(k, chunkCount), sampled.Count);

        // 小文件:全查,覆盖率 100%
        if (chunkCount <= FastVerify.FullCoverageChunkLimit)
        {
            Assert.Equal(chunkCount, sampled.Count);
            return;
        }

        // 大文件:目标半覆盖,但受 MaxSampleCount 封顶(避免超大文件退化成近全量读盘)
        Assert.Equal(Math.Min(FastVerify.MaxSampleCount, (chunkCount + 1) / 2), k);
        Assert.True(k <= FastVerify.MaxSampleCount);

        // 关键性质:覆盖率显著高于旧的固定 3 片(旧规则 186 片只有 1.6%)
        var newCoverage = (double)sampled.Count / chunkCount;
        var oldCoverage = 3.0 / chunkCount;
        Assert.True(
            newCoverage > oldCoverage,
            $"{chunkCount} 片:新覆盖率 {newCoverage:P1} 应高于旧固定 3 片的 {oldCoverage:P1}");
    }

    /// <summary>
    /// 覆盖率与旧规则的量化对比:模拟 186 片(真实 18.6 GB pak)时,
    /// 旧规则 1.6% → 新规则 34.4%,即"最该查的大文件"不再被放过。
    /// </summary>
    [Fact]
    public void 大文件覆盖率相对旧规则显著提升_以真实186片pak为例()
    {
        const int chunks = 186; // Client/Content/HD/pakchunk70-HD-WindowsNoEditor.pak 实测片数
        var oldCount = FastVerify.SelectSampledChunks(Chunks(chunks, 8, new byte[chunks * 8]), 3).Count;
        var newCount = FastVerify.SelectSampledChunks(Chunks(chunks, 8, new byte[chunks * 8]), FastVerify.SampleCountFor(chunks)).Count;

        Assert.Equal(3, oldCount);
        Assert.Equal(64, newCount); // min(64, ceil(186/2)=93)
        Assert.True(newCount * 100.0 / chunks > 34, "186 片时覆盖率应超过 34%");
    }

    /// <summary>
    /// 修正后每个文件的覆盖率都<b>不劣于</b>旧的固定 3 片规则(避免"修一处坏一处")。
    /// </summary>
    [Theory]
    [InlineData(5)]
    [InlineData(16)]
    [InlineData(68)]
    [InlineData(186)]
    public void 新规则的逐文件覆盖率不劣于旧的固定3片(int chunkCount)
    {
        var chunks = Chunks(chunkCount, chunkSize: 8, data: new byte[chunkCount * 8]);
        var oldCount = FastVerify.SelectSampledChunks(chunks, 3).Count;
        var newCount = FastVerify.SelectSampledChunks(chunks, FastVerify.SampleCountFor(chunkCount)).Count;

        Assert.True(
            newCount >= oldCount,
            $"{chunkCount} 片:新规则采样 {newCount} 片,不应少于旧规则的 {oldCount} 片");
    }

    /// <summary>采样到的分片损坏必须被发现(否则快速校验就失去了意义)。</summary>
    [Fact]
    public void 开启快速校验_采样分片损坏必被发现()
    {
        var data = Encoding.UTF8.GetBytes("0123456789abcdefghijklmnopqrstuvwxyzABCDEFGHIJ");
        var damaged = (byte[])data.Clone();
        damaged[16] = (byte)'X'; // 第 3 个分片(index 2)—— 正是采样点
        var path = WriteFile("sampled_bad.bin", damaged);
        var entry = Entry("sampled_bad.bin", data.Length, data);
        entry.ChunkInfos.AddRange(Chunks(5, chunkSize: 8, data: data));

        Assert.False(UpdateInstaller.IsLocalFileIntact(path, entry, fastVerify: true));
    }

    [Fact]
    public void 文件缺失一律判为需要下载()
    {
        var entry = Entry("gone.bin", 32);
        entry.ChunkInfos.AddRange(Chunks(4, chunkSize: 8, data: new byte[32]));
        var missing = Path.Combine(_root, "gone.bin");

        Assert.False(UpdateInstaller.IsLocalFileIntact(missing, entry, fastVerify: false));
        Assert.False(UpdateInstaller.IsLocalFileIntact(missing, entry, fastVerify: true));
    }

    // ---------- ComputeDiff 集成 ----------

    [Fact]
    public void ComputeDiff_关闭快速校验时用全文件校验()
    {
        var data = Encoding.UTF8.GetBytes("0123456789abcdefghijklmnopqrstuv");
        var damaged = (byte[])data.Clone();
        damaged[8] = (byte)'X';
        WriteFile("game/mid.bin", damaged);

        var diff = new UpdateInstaller().ComputeDiff(
            Manifest("mid.bin", data),
            Path.Combine(_root, "game"),
            fastVerify: false);

        Assert.Single(diff.ToDownload);
    }

    [Fact]
    public void ComputeDiff_开启快速校验时完好文件不进下载列表()
    {
        var data = Encoding.UTF8.GetBytes("0123456789abcdefghijklmnopqrstuv");
        WriteFile("game2/ok.bin", data);

        var diff = new UpdateInstaller().ComputeDiff(
            Manifest("ok.bin", data),
            Path.Combine(_root, "game2"),
            fastVerify: true);

        Assert.Empty(diff.ToDownload);
    }

    // ---------- 分片完整覆盖(对齐 Haiyu 1.4.5) ----------

    /// <summary>
    /// 完整覆盖:分片从 0 起首尾相接、末端正好等于文件大小、每片都带哈希。
    /// 这是"可以跳过整文件 MD5"的唯一前提。
    /// </summary>
    [Fact]
    public void 覆盖判定_连续且末端吻合时为真()
    {
        var data = new byte[32];
        var entry = Entry("a.bin", data.Length);
        entry.ChunkInfos.AddRange(Chunks(4, chunkSize: 8, data: data));

        Assert.True(FastVerify.HasCompleteChunkCoverage(entry));
    }

    /// <summary>中间有空洞(不连续)时不算完整覆盖 —— 有字节没被验证过。</summary>
    [Fact]
    public void 覆盖判定_存在空洞时为假()
    {
        var data = new byte[32];
        var entry = Entry("a.bin", data.Length);
        var chunks = Chunks(4, chunkSize: 8, data: data);
        // 去掉中间一片,制造 8..15 的空洞
        chunks.RemoveAt(1);
        entry.ChunkInfos.AddRange(chunks);

        Assert.False(FastVerify.HasCompleteChunkCoverage(entry));
    }

    /// <summary>末端不足(分片只到 size-2)不算完整覆盖。</summary>
    [Fact]
    public void 覆盖判定_末端不足时为假()
    {
        var data = new byte[32];
        var entry = Entry("a.bin", data.Length);
        var chunks = Chunks(4, chunkSize: 8, data: data);
        var last = chunks[^1];
        chunks[^1] = new GameChunkInfo { Start = last.Start, End = last.End - 2, Md5 = last.Md5 };
        entry.ChunkInfos.AddRange(chunks);

        Assert.False(FastVerify.HasCompleteChunkCoverage(entry));
    }

    /// <summary>任一分片缺哈希时不能算完整覆盖(那片内容没被验证)。</summary>
    [Fact]
    public void 覆盖判定_分片缺哈希时为假()
    {
        var data = new byte[32];
        var entry = Entry("a.bin", data.Length);
        entry.ChunkInfos.AddRange(WithoutHash(Chunks(4, chunkSize: 8, data: data), 2));

        Assert.False(FastVerify.HasCompleteChunkCoverage(entry));
    }

    /// <summary>没有分片信息 / size 未知时一律不算覆盖(交回整文件 MD5)。</summary>
    [Fact]
    public void 覆盖判定_无分片或大小未知时为假()
    {
        Assert.False(FastVerify.HasCompleteChunkCoverage(Entry("a.bin", 32)));

        var unknownSize = Entry("b.bin", 0);
        unknownSize.ChunkInfos.AddRange(Chunks(4, chunkSize: 8, data: new byte[32]));
        Assert.False(FastVerify.HasCompleteChunkCoverage(unknownSize));
    }

    /// <summary>分片顺序打乱不影响覆盖判定(内部按 Start 排序)。</summary>
    [Fact]
    public void 覆盖判定_乱序分片仍判为完整()
    {
        var data = new byte[32];
        var entry = Entry("a.bin", data.Length);
        var chunks = Chunks(4, chunkSize: 8, data: data);
        chunks.Reverse();
        entry.ChunkInfos.AddRange(chunks);

        Assert.True(FastVerify.HasCompleteChunkCoverage(entry));
    }

    /// <summary>
    /// <b>核心:完整覆盖时用分片校验能判出内容损坏</b>(不依赖整文件 md5),
    /// 且内容正确时返回 true —— 证明跳过整文件 MD5 没有放宽判定。
    /// </summary>
    [Fact]
    public void 全分片校验_内容不符时判为损坏()
    {
        var data = Encoding.UTF8.GetBytes("0123456789abcdefghijklmnopqrstuv");
        var path = WriteFile("cover/ok.bin", data);

        var good = Entry("ok.bin", data.Length);
        good.ChunkInfos.AddRange(Chunks(4, chunkSize: 8, data: data));
        Assert.True(FastVerify.HasCompleteChunkCoverage(good));
        Assert.True(FastVerify.VerifyAllChunks(path, good));

        // 篡改中间一个字节:分片哈希必须发现,且不依赖整文件 md5(这里是空)
        var corrupted = (byte[])data.Clone();
        corrupted[10] ^= 0xFF;
        var bad = WriteFile("cover/bad.bin", corrupted);
        var badEntry = Entry("bad.bin", corrupted.Length);
        badEntry.ChunkInfos.AddRange(Chunks(4, chunkSize: 8, data: data)); // 用正确内容的哈希

        Assert.False(FastVerify.VerifyAllChunks(bad, badEntry));
    }

    /// <summary>不满足完整覆盖时 <see cref="FastVerify.VerifyAllChunks"/> 必须拒绝,交由整文件 MD5。</summary>
    [Fact]
    public void 全分片校验_覆盖不完整时拒绝()
    {
        var data = new byte[32];
        var path = WriteFile("cover/partial.bin", data);
        var entry = Entry("partial.bin", data.Length);
        var chunks = Chunks(4, chunkSize: 8, data: data);
        chunks.RemoveAt(0);
        entry.ChunkInfos.AddRange(chunks);

        Assert.False(FastVerify.VerifyAllChunks(path, entry));
    }

    /// <summary>文件长度与 size 不符时立即判损坏,不白读整份文件。</summary>
    [Fact]
    public void 全分片校验_长度不符时判为损坏()
    {
        var data = new byte[32];
        var path = WriteFile("cover/short.bin", data[..16]);
        var entry = Entry("short.bin", data.Length);
        entry.ChunkInfos.AddRange(Chunks(4, chunkSize: 8, data: data));

        Assert.False(FastVerify.VerifyAllChunks(path, entry));
    }

    // ---------- 辅助 ----------

    private GameManifest Manifest(string path, byte[] data)
    {
        var entry = Entry(path, data.Length);
        entry.ChunkInfos.AddRange(Chunks(4, chunkSize: 8, data: data));
        // 清单里的文件级 MD5 也要正确,便于验证回退路径
        entry.Md5 = Convert.ToHexStringLower(MD5.HashData(data));
        return new GameManifest { Version = "1.0.0", Files = [entry] };
    }

    private static GameFileEntry Entry(string path, long size, byte[]? correctContent = null) => new()
    {
        Path = path,
        Size = size,
        // 文件级 MD5:给出正确内容时填入正确哈希,否则留空(仅分片校验有意义)
        Md5 = correctContent is null ? "" : Convert.ToHexStringLower(System.Security.Cryptography.MD5.HashData(correctContent)),
    };

    /// <summary>把数据切成 count 个等长分片,并填好每片 MD5。</summary>
    private static List<GameChunkInfo> Chunks(int count, int chunkSize, byte[] data)
    {
        var list = new List<GameChunkInfo>(count);
        for (var i = 0; i < count; i++)
        {
            var start = (long)i * chunkSize;
            var end = Math.Min(start + chunkSize, data.Length) - 1;
            if (end < start)
            {
                break;
            }
            var slice = data[(int)start..((int)end + 1)];
            list.Add(new GameChunkInfo
            {
                Start = start,
                End = end,
                Md5 = Convert.ToHexStringLower(MD5.HashData(slice)),
            });
        }
        return list;
    }

    /// <summary>
    /// L1:下载器改用 <see cref="FastVerify.ChunkHashMatchesAsync"/> 真异步读盘;它必须与同步版逐条同判
    /// (匹配 / 哈希不符 / 无哈希 / 文件短于分片末尾)。
    /// </summary>
    [Fact]
    public async Task ChunkHashMatchesAsync_Agrees_With_Sync_Version()
    {
        var payload = new byte[300_000];
        new Random(3).NextBytes(payload);
        var path = WriteFile("async.bin", payload);
        string Md5(int start, int length) =>
            Convert.ToHexStringLower(System.Security.Cryptography.MD5.HashData(payload.AsSpan(start, length)));
        var cases = new[]
        {
            new GameChunkInfo { Start = 0, End = 199_999, Md5 = Md5(0, 200_000) },
            new GameChunkInfo { Start = 200_000, End = 299_999, Md5 = Md5(200_000, 100_000) },
            new GameChunkInfo { Start = 0, End = 99, Md5 = new string('0', 32) },
            new GameChunkInfo { Start = 0, End = 99, Md5 = "" },
            new GameChunkInfo { Start = 250_000, End = 349_999, Md5 = Md5(250_000, 50_000) },
        };

        await using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, useAsync: true);
        foreach (var chunk in cases)
        {
            var syncResult = FastVerify.ChunkHashMatches(file, chunk);
            var asyncResult = await FastVerify.ChunkHashMatchesAsync(file, chunk);
            Assert.Equal(syncResult, asyncResult);
        }
        Assert.True(await FastVerify.ChunkHashMatchesAsync(file, cases[0]));
        Assert.False(await FastVerify.ChunkHashMatchesAsync(file, cases[4]));
    }

    /// <summary>把指定索引的分片哈希清空(<see cref="GameChunkInfo.Md5"/> 是 init-only,只能重建)。</summary>
    private static List<GameChunkInfo> WithoutHash(IReadOnlyList<GameChunkInfo> chunks, int index)
    {
        var rebuilt = new List<GameChunkInfo>(chunks.Count);
        for (var i = 0; i < chunks.Count; i++)
        {
            var c = chunks[i];
            rebuilt.Add(new GameChunkInfo
            {
                Start = c.Start,
                End = c.End,
                Md5 = i == index ? "" : c.Md5,
            });
        }
        return rebuilt;
    }

    private string WriteFile(string relative, byte[] content)
    {
        var path = Path.Combine(_root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, content);
        return path;
    }
}
