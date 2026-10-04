using System.Text;
using McKuro.Core.Models.Roles;
using McKuro.Services;

namespace McKuro.Tests;

/// <summary>
/// 角色图标磁盘缓存测试:索引读写 / ResolveIcon / 下载去重 / 失败静默。
/// 通过注入下载委托避免真实网络请求(临时目录,测试后清理)。
/// </summary>
public class IconDiskCacheServiceTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "mc_kuro_icon_cache_" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_dir))
            {
                Directory.Delete(_dir, recursive: true);
            }
        }
        catch
        {
            // 清理失败不影响测试结论
        }
    }

    private sealed class FakeDownloader
    {
        private readonly Dictionary<string, byte[]> _bytesByUrl;
        public List<string> Requested { get; } = [];

        public FakeDownloader(Dictionary<string, byte[]> bytesByUrl) => _bytesByUrl = bytesByUrl;

        public Task<byte[]?> Download(string url, CancellationToken ct)
        {
            Requested.Add(url);
            return Task.FromResult(_bytesByUrl.TryGetValue(url, out var b) ? b : null);
        }
    }

    private static RoleDetail BuildRole() => new()
    {
        Role = new RoleInfo
        {
            RoleName = "莫宁",
            RolePicUrl = "https://img.kurobbs.com/role.png",
        },
        WeaponData = new WeaponData
        {
            Weapon = new WeaponInfo
            {
                WeaponName = "千古洑流",
                WeaponIcon = "https://img.kurobbs.com/weapon.png",
            },
        },
        Skills =
        [
            new SkillInfo { Skill = new SkillBase { SkillName = "普攻", IconUrl = "https://img.kurobbs.com/skill1.png" } },
        ],
        Chains =
        [
            new ChainInfo { ChainName = "一链", IconUrl = "https://img.kurobbs.com/chain1.png" },
        ],
        Attributes =
        [
            new RoleAttribute { AttributeName = "暴击", IconUrl = "https://img.kurobbs.com/attr1.png" },
        ],
        PhantomData = new PhantomData
        {
            Phantoms =
            [
                new EchoInfo
                {
                    PhantomProp = new PhantomPropInfo
                    {
                        PhantomName = "啸谷幼猿",
                        IconUrl = "https://img.kurobbs.com/echo.png",
                    },
                },
            ],
        },
    };

    private static byte[] Png(string tag) => Encoding.UTF8.GetBytes("fake-image-" + tag);

    [Fact]
    public void Safe_Replaces_Invalid_File_Name_Chars()
    {
        var safe = IconDiskCacheService.Safe("武器/剑:1?*");
        foreach (var c in safe)
        {
            Assert.False(Path.GetInvalidFileNameChars().Contains(c));
        }
        Assert.Equal("武器_剑_1__", safe);
    }

    [Fact]
    public void ResolveIcon_Falls_Back_To_Url_When_Not_Cached()
    {
        var service = new IconDiskCacheService(_dir);
        Assert.Equal("https://guide-res/fallback.png",
            service.ResolveIcon(IconDiskCacheService.CategoryWeapon, "千古洑流", "https://guide-res/fallback.png"));
        Assert.Null(service.GetCachedIconPath(IconDiskCacheService.CategoryWeapon, "千古洑流"));
    }

    [Fact]
    public async Task CacheRoleIconsAsync_Stores_All_Categories_And_Persists_Index()
    {
        var downloader = new FakeDownloader(new Dictionary<string, byte[]>
        {
            ["https://img.kurobbs.com/role.png"] = Png("role"),
            ["https://img.kurobbs.com/weapon.png"] = Png("weapon"),
            ["https://img.kurobbs.com/skill1.png"] = Png("skill"),
            ["https://img.kurobbs.com/chain1.png"] = Png("chain"),
            ["https://img.kurobbs.com/attr1.png"] = Png("attr"),
            ["https://img.kurobbs.com/echo.png"] = Png("echo"),
        });
        var service = new IconDiskCacheService(_dir, downloader.Download);

        await service.CacheRoleIconsAsync(BuildRole());

        // 各分类按名称命中且本地文件存在
        var rolePath = service.GetCachedIconPath(IconDiskCacheService.CategoryRole, "莫宁");
        var weaponPath = service.GetCachedIconPath(IconDiskCacheService.CategoryWeapon, "千古洑流");
        var skillPath = service.GetCachedIconPath(IconDiskCacheService.CategorySkill, "普攻");
        var attrPath = service.GetCachedIconPath(IconDiskCacheService.CategoryAttr, "暴击");
        var echoPath = service.GetCachedIconPath(IconDiskCacheService.CategoryEcho, "啸谷幼猿");
        Assert.NotNull(rolePath);
        Assert.NotNull(weaponPath);
        Assert.NotNull(skillPath);
        Assert.NotNull(attrPath);
        Assert.NotNull(echoPath);
        foreach (var p in new[] { rolePath, weaponPath, skillPath, attrPath, echoPath })
        {
            Assert.True(File.Exists(p));
        }

        // 共鸣链:白线稿/白图污染问题未解决前不进磁盘缓存,预下载阶段就跳过(与 ResolveIcon 的绕过策略对齐)
        Assert.DoesNotContain("https://img.kurobbs.com/chain1.png", downloader.Requested);
        Assert.Null(service.GetCachedIconPath(IconDiskCacheService.CategoryChain, "一链"));

        // 其余 5 个 http 图标全部请求,且 ResolveIcon 命中本地路径
        Assert.Equal(5, downloader.Requested.Count);
        Assert.Equal(rolePath, service.ResolveIcon(IconDiskCacheService.CategoryRole, "莫宁", "https://fallback/role.png"));

        // 索引持久化:新实例读同一目录仍能按名称命中
        var reloaded = new IconDiskCacheService(_dir, downloader.Download);
        Assert.Equal(weaponPath, reloaded.GetCachedIconPath(IconDiskCacheService.CategoryWeapon, "千古洑流"));
        Assert.Equal(echoPath, reloaded.GetCachedIconPath(IconDiskCacheService.CategoryEcho, "啸谷幼猿"));
        Assert.True(File.Exists(Path.Combine(_dir, "index.json")));
    }

    [Fact]
    public async Task CacheRoleIconsAsync_Skips_Non_Http_And_Already_Cached()
    {
        var downloader = new FakeDownloader(new Dictionary<string, byte[]>
        {
            ["https://img.kurobbs.com/weapon.png"] = Png("weapon"),
            ["https://img.kurobbs.com/skill1.png"] = Png("skill"),
            ["https://img.kurobbs.com/chain1.png"] = Png("chain"),
            ["https://img.kurobbs.com/attr1.png"] = Png("attr"),
            ["https://img.kurobbs.com/echo.png"] = Png("echo"),
        });
        var role = BuildRole();
        // 本地路径图标(非 http)不应触发下载
        Directory.CreateDirectory(_dir);
        var localRolePic = Path.Combine(_dir, "local_role.png");
        File.WriteAllBytes(localRolePic, Png("local"));
        role.Role!.RolePicUrl = localRolePic;

        var service = new IconDiskCacheService(_dir, downloader.Download);
        await service.CacheRoleIconsAsync(role);

        // 非 http 的本地路径未请求;仅 4 个 http 图标被下载(立绘为本地路径,chain 有意跳过)
        Assert.DoesNotContain(downloader.Requested, u => u.StartsWith(_dir, StringComparison.Ordinal));
        Assert.Contains("https://img.kurobbs.com/weapon.png", downloader.Requested);
        Assert.Equal(4, downloader.Requested.Count);

        // 再次缓存同角色:已缓存名称不重复下载
        await service.CacheRoleIconsAsync(role);
        Assert.Equal(4, downloader.Requested.Count);
    }

    [Fact]
    public async Task CacheRoleIconsAsync_Ignores_Download_Failure()
    {
        var downloader = new FakeDownloader(new Dictionary<string, byte[]>()); // 所有 URL 返回 null
        var service = new IconDiskCacheService(_dir, downloader.Download);

        await service.CacheRoleIconsAsync(BuildRole()); // 不应抛异常

        // 5 个 http 图标都尝试过(chain 有意不下载),失败后不留任何条目
        Assert.Equal(5, downloader.Requested.Count);
        Assert.Null(service.GetCachedIconPath(IconDiskCacheService.CategoryWeapon, "千古洑流"));
    }

    [Fact]
    public async Task CacheRoleIconsAsync_Empty_Role_Is_Noop()
    {
        var downloader = new FakeDownloader(new Dictionary<string, byte[]>());
        var service = new IconDiskCacheService(_dir, downloader.Download);

        await service.CacheRoleIconsAsync(new RoleDetail());

        Assert.Empty(downloader.Requested);
    }

    [Fact]
    public async Task ResolveIcon_Matches_By_Name_Across_Domains()
    {
        // 库街区(A 域名)缓存 → mcguide(B 域名)同名称请求命中本地路径
        var downloader = new FakeDownloader(new Dictionary<string, byte[]>
        {
            ["https://img.kurobbs.com/weapon.png"] = Png("weapon"),
        });
        var service = new IconDiskCacheService(_dir, downloader.Download);

        var kujiequRole = BuildRole();
        await service.CacheRoleIconsAsync(kujiequRole);

        // mcguide 返回的同名武器(B 域名 URL)被替换为本地缓存路径
        var resolved = service.ResolveIcon(
            IconDiskCacheService.CategoryWeapon,
            "千古洑流",
            "https://guide-res.aki-game.com/weapon.png");
        Assert.NotEqual("https://guide-res.aki-game.com/weapon.png", resolved);
        Assert.True(File.Exists(resolved));
    }

    [Fact]
    public async Task CacheUrlAsync_Downloads_Persists_And_Skips_Already_Cached()
    {
        // 玩家头像场景(参照 Java WutheringWavesTool imageBuffer):URL → 本地文件,按稳定 key 复用
        var downloader = new FakeDownloader(new Dictionary<string, byte[]>
        {
            ["https://web-static.kurobbs.com/profile_picture/1.png"] = Png("avatar"),
        });
        var service = new IconDiskCacheService(_dir, downloader.Download);

        // 首次:下载落盘并命中本地路径
        await service.CacheUrlAsync(IconDiskCacheService.CategoryAvatar, "15714568",
            "https://web-static.kurobbs.com/profile_picture/1.png");
        var path = service.GetCachedIconPath(IconDiskCacheService.CategoryAvatar, "15714568");
        Assert.NotNull(path);
        Assert.True(File.Exists(path));
        Assert.Equal(Png("avatar"), await File.ReadAllBytesAsync(path!));

        // 二次:已缓存不再下载;换 URL 同 key 也命中本地缓存(头像地址变化仍用旧图,直到手动清理)
        await service.CacheUrlAsync(IconDiskCacheService.CategoryAvatar, "15714568",
            "https://web-static.kurobbs.com/profile_picture/2.png");
        Assert.Single(downloader.Requested);

        // 索引持久化:新实例读同一目录仍命中
        var reloaded = new IconDiskCacheService(_dir, downloader.Download);
        Assert.Equal(path, reloaded.GetCachedIconPath(IconDiskCacheService.CategoryAvatar, "15714568"));
    }

    [Fact]
    public async Task CacheUrlAsync_Ignores_Non_Http_And_Failures()
    {
        var downloader = new FakeDownloader(new Dictionary<string, byte[]>()); // 全部返回 null
        var service = new IconDiskCacheService(_dir, downloader.Download);

        // 非 http(本地路径)/空 URL 不触发下载
        await service.CacheUrlAsync(IconDiskCacheService.CategoryAvatar, "a", "E:\\not\\http.png");
        await service.CacheUrlAsync(IconDiskCacheService.CategoryAvatar, "a", "");
        // 下载失败静默,不留缓存条目
        await service.CacheUrlAsync(IconDiskCacheService.CategoryAvatar, "b", "https://img.kurobbs.com/missing.png");

        // 仅 http URL 被请求(失败返回 null);本地路径与空 URL 未请求
        var requested = Assert.Single(downloader.Requested);
        Assert.Equal("https://img.kurobbs.com/missing.png", requested);
        Assert.Null(service.GetCachedIconPath(IconDiskCacheService.CategoryAvatar, "a"));
        Assert.Null(service.GetCachedIconPath(IconDiskCacheService.CategoryAvatar, "b"));
    }

    // ---------------- 分类名校验:非法 category 不得逃逸缓存目录 ----------------

    [Theory]
    [InlineData("../evil")]
    [InlineData("..\\evil")]
    [InlineData("a/b")]
    [InlineData("a\\b")]
    [InlineData("C:/evil")]
    [InlineData("")]
    [InlineData("   ")]
    public async Task CacheUrlAsync_Rejects_Unsafe_Category_Before_Downloading(string category)
    {
        // 真实缓存目录是 <outside>\icon_cache:非法分类若未被拦住,Path.Combine 会写到 <outside> 之外/之上
        var outside = Path.Combine(Path.GetTempPath(), "mc_kuro_escape_" + Guid.NewGuid().ToString("N"));
        var cacheDir = Path.Combine(outside, "icon_cache");
        Directory.CreateDirectory(cacheDir);
        try
        {
            var downloader = new FakeDownloader(new Dictionary<string, byte[]>
            {
                ["https://img.kurobbs.com/x.png"] = Png("x"),
            });
            var service = new IconDiskCacheService(cacheDir, downloader.Download);

            await service.CacheUrlAsync(category, "x", "https://img.kurobbs.com/x.png");

            // 非法分类在「发起下载之前」就被拒绝:一次网络请求都没发起,也没有任何落盘/索引
            Assert.Empty(downloader.Requested);
            Assert.Null(service.GetCachedIconPath(category, "x"));
            Assert.False(File.Exists(Path.Combine(cacheDir, "index.json")));
            Assert.Empty(Directory.GetDirectories(cacheDir));
            Assert.Empty(Directory.GetFiles(cacheDir));
            // 缓存目录之外(含上级 escape 目录)没有多出任何条目
            var outsideDirs = Directory.GetDirectories(outside);
            Assert.Single(outsideDirs);
            Assert.Equal("icon_cache", Path.GetFileName(outsideDirs[0]));
            Assert.Empty(Directory.GetFiles(outside));

            // 合法分类不受影响:仍能正常下载落盘
            await service.CacheUrlAsync(IconDiskCacheService.CategoryAvatar, "x", "https://img.kurobbs.com/x.png");
            Assert.Single(downloader.Requested);
            Assert.NotNull(service.GetCachedIconPath(IconDiskCacheService.CategoryAvatar, "x"));
        }
        finally
        {
            try { Directory.Delete(outside, recursive: true); } catch { /* 清理失败不影响测试结论 */ }
        }
    }

    [Fact]
    public async Task CacheRoleIconsAsync_Only_Emits_Safe_Categories()
    {
        // 预下载枚举出的分类必须全部是合法目录名(防御与 CacheOneAsync 的校验同源)
        var downloader = new FakeDownloader(new Dictionary<string, byte[]>());
        var service = new IconDiskCacheService(_dir, downloader.Download);

        await service.CacheRoleIconsAsync(BuildRole());

        // 角色详情各分类都过了 IsSafeCategory:没有任何目录逃逸,缓存根下不出现 ".."/"a/b" 之类条目
        if (Directory.Exists(_dir))
        {
            foreach (var sub in Directory.GetDirectories(_dir))
            {
                var name = Path.GetFileName(sub)!;
                Assert.False(name.Contains("..", StringComparison.Ordinal));
                Assert.False(name.Contains('/'));
                Assert.False(name.Contains('\\'));
            }
        }
    }

    // ---------------- 在途下载去重:同一 key 并发只下载一次 ----------------

    [Fact]
    public async Task CacheUrlAsync_Concurrent_Same_Key_Downloads_Once()
    {
        // ResolveIcon 本身是纯缓存查询(不下载),去重发生在唯一的写入口 CacheUrlAsync 上
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<byte[]?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        var service = new IconDiskCacheService(_dir, async (_, _) =>
        {
            Interlocked.Increment(ref calls);
            entered.TrySetResult();
            return await release.Task;
        });

        const string url = "https://img.kurobbs.com/concurrent.png";
        var first = service.CacheUrlAsync(IconDiskCacheService.CategoryAvatar, "shared", url);
        await entered.Task; // 首个请求已登记在途并真的开始下载(此刻它的字节尚未到手,缓存还没写好)

        var rest = Enumerable.Range(0, 8)
            .Select(_ => service.CacheUrlAsync(IconDiskCacheService.CategoryAvatar, "shared", url))
            .ToArray();

        release.SetResult(Png("icon"));
        var all = new List<Task> { first };
        all.AddRange(rest);
        await Task.WhenAll(all);

        // 9 个并发请求共享同一次下载,只落盘一次
        Assert.Equal(1, calls);
        var path = service.GetCachedIconPath(IconDiskCacheService.CategoryAvatar, "shared");
        Assert.NotNull(path);
        Assert.True(File.Exists(path));

        // 在途登记完成后被移除:再来同 key 请求直接命中磁盘缓存,仍不重复下载
        await service.CacheUrlAsync(IconDiskCacheService.CategoryAvatar, "shared", url);
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task CacheRoleIconsAsync_Concurrent_Calls_Download_Each_Icon_Once()
    {
        // 两次并发「进角色详情页」预下载同一角色:每个图标只应拉一次(另一路共享在途或命中缓存)
        var requested = new List<string>();
        var gate = new object();
        var release = new TaskCompletionSource<byte[]?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var service = new IconDiskCacheService(_dir, async (url, _) =>
        {
            lock (gate)
            {
                requested.Add(url);
            }
            return await release.Task;
        });

        var t1 = service.CacheRoleIconsAsync(BuildRole());
        var t2 = service.CacheRoleIconsAsync(BuildRole());
        release.SetResult(Png("icon")); // 首次下载放行后,后续图标立即完成 → 并发压力仍在
        await Task.WhenAll(t1, t2);

        Assert.Equal(requested.Distinct(StringComparer.Ordinal).Count(), requested.Count); // 无重复 URL
        Assert.Equal(5, requested.Count); // chain 有意跳过,其余 5 个图标各下载一次
    }

    // ---------------- 下载大小上限 ----------------

    [Fact]
    public async Task ReadCappedAsync_Rejects_Oversize_And_Allows_Within_Limit()
    {
        const int cap = (int)IconDiskCacheService.MaxDownloadBytes;
        var oversize = new byte[cap + 1];

        // ① Content-Length 已声明超限:直接放弃,连流都不读(不分配缓冲区)
        var declared = new MemoryStream(oversize);
        Assert.Null(await IconDiskCacheService.ReadCappedAsync(declared, cap + 1));
        Assert.Equal(0L, declared.Position);

        // ② 未声明 / 谎报 Content-Length:按实际累计字节超限中止
        Assert.Null(await IconDiskCacheService.ReadCappedAsync(new MemoryStream(oversize), declaredLength: null));
        Assert.Null(await IconDiskCacheService.ReadCappedAsync(new MemoryStream(oversize), declaredLength: 16));

        // ③ 上限内正常返回全部字节(声明长度与未声明两种路径)
        var small = new byte[4096];
        new Random(7).NextBytes(small);
        Assert.Equal(small, await IconDiskCacheService.ReadCappedAsync(new MemoryStream(small), small.Length));
        Assert.Equal(small, await IconDiskCacheService.ReadCappedAsync(new MemoryStream(small), declaredLength: null));
        // 恰好等于上限:允许(边界本身不算超限)
        Assert.Equal(cap, (await IconDiskCacheService.ReadCappedAsync(new MemoryStream(oversize, 0, cap), cap))?.Length);
    }

    [Fact]
    public async Task CacheUrlAsync_Treats_Oversize_As_Download_Failure_And_Leaves_No_Cache()
    {
        // 注入委托返回 null(等价于 DefaultDownload 命中上限后的结果):静默跳过,不写缓存条目
        var service = new IconDiskCacheService(_dir, (_, _) => Task.FromResult<byte[]?>(null));
        await service.CacheUrlAsync(IconDiskCacheService.CategoryAvatar, "big", "https://img.kurobbs.com/huge.png");

        Assert.Null(service.GetCachedIconPath(IconDiskCacheService.CategoryAvatar, "big"));
        Assert.False(Directory.Exists(Path.Combine(_dir, IconDiskCacheService.CategoryAvatar)));
    }

    // ---------------- 原子落盘:不留半截/临时文件 ----------------

    [Fact]
    public async Task Store_Writes_Atomically_Without_Leftover_Temp_Files()
    {
        var downloader = new FakeDownloader(new Dictionary<string, byte[]>
        {
            ["https://img.kurobbs.com/w.png"] = Png("weapon"),
        });
        var service = new IconDiskCacheService(_dir, downloader.Download);
        var finalPath = Path.Combine(_dir, IconDiskCacheService.CategoryWeapon, "千古洑流.png");

        await service.CacheUrlAsync(IconDiskCacheService.CategoryWeapon, "千古洑流", "https://img.kurobbs.com/w.png");

        var dir = Path.Combine(_dir, IconDiskCacheService.CategoryWeapon);
        var files = Directory.GetFiles(dir);
        // 落盘走「.tmp-{guid} + File.Move 覆盖」:目录里只应有最终的 .png,不应残留任何临时文件
        Assert.DoesNotContain(files, f => Path.GetFileName(f).Contains(".tmp-", StringComparison.Ordinal));
        Assert.Single(files);
        Assert.Equal(Png("weapon"), await File.ReadAllBytesAsync(finalPath));

        // 覆盖写:清掉索引条目后伪造一个「半截 PNG」残留在目标路径(崩溃现场),
        // 重新缓存必须用原子替换把它整体换掉,而不是在旧文件上续写/留下临时文件
        service.PurgeCategory(IconDiskCacheService.CategoryWeapon);
        Directory.CreateDirectory(dir); // PurgeCategory 会顺带删掉空目录
        File.WriteAllBytes(finalPath, [0x89, 0x50, 0x4E]);
        await new IconDiskCacheService(_dir, downloader.Download)
            .CacheUrlAsync(IconDiskCacheService.CategoryWeapon, "千古洑流", "https://img.kurobbs.com/w.png");

        Assert.Equal(Png("weapon"), await File.ReadAllBytesAsync(finalPath));
        Assert.DoesNotContain(Directory.GetFiles(dir), f => Path.GetFileName(f).Contains(".tmp-", StringComparison.Ordinal));
    }
}

/// <summary>
/// 共鸣链图标白图污染的回归测试(见 <c>IconDiskCacheService.ResolveIcon/LooksLikeBlankOrWhite/PurgeCategory</c>)。
/// <para>原位于 <c>GuideTeammateMappingTests.cs</c>(为遵守当时的文件范围约定),现归位到本文件:
/// 与上面的磁盘缓存基础测试同属 <see cref="IconDiskCacheService"/> 的验收证据。</para>
/// </summary>
public class IconDiskCacheChainWhiteGuardTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "mc_kuro_chain_guard_" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_dir))
            {
                Directory.Delete(_dir, recursive: true);
            }
        }
        catch
        {
            // 清理失败不影响测试结论
        }
    }

    // ---------------- 纯托管 PNG 编码器(仅测试用,产出合法 PNG 供解码器验证) ----------------

    private static byte[] PngBytes(int width, int height, byte r, byte g, byte b, byte a)
        => PngBytes(width, height, (_, _) => (r, g, b, a));

    /// <summary>按像素委托生成 PNG,用于构造「白底 + 深色轮廓」这类接近真实的图标。</summary>
    private static byte[] PngBytes(int width, int height, Func<int, int, (byte R, byte G, byte B, byte A)> pixel)
    {
        var stride = width * 4;
        var raw = new byte[height * (stride + 1)];
        for (var y = 0; y < height; y++)
        {
            var row = y * (stride + 1);
            raw[row] = 0; // 滤波类型 0(None)
            for (var x = 0; x < width; x++)
            {
                var (r, g, b, a) = pixel(x, y);
                var p = row + 1 + x * 4;
                raw[p] = r;
                raw[p + 1] = g;
                raw[p + 2] = b;
                raw[p + 3] = a;
            }
        }

        using var ms = new MemoryStream();
        ms.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);

        var ihdr = new byte[13];
        WriteBe32(ihdr, 0, width);
        WriteBe32(ihdr, 4, height);
        ihdr[8] = 8;  // 位深
        ihdr[9] = 6;  // 颜色类型 6 = RGBA
        WriteChunk(ms, "IHDR", ihdr);

        using (var compressed = new MemoryStream())
        {
            using (var z = new System.IO.Compression.ZLibStream(compressed, System.IO.Compression.CompressionLevel.Optimal, leaveOpen: true))
            {
                z.Write(raw, 0, raw.Length);
            }
            WriteChunk(ms, "IDAT", compressed.ToArray());
        }

        WriteChunk(ms, "IEND", []);
        return ms.ToArray();
    }

    private static void WriteChunk(Stream s, string type, byte[] data)
    {
        var header = new byte[4];
        WriteBe32(header, 0, data.Length);
        s.Write(header);
        var typeBytes = Encoding.ASCII.GetBytes(type);
        s.Write(typeBytes);
        s.Write(data);
        var crcInput = new byte[typeBytes.Length + data.Length];
        typeBytes.CopyTo(crcInput, 0);
        data.CopyTo(crcInput, typeBytes.Length);
        var crc = new byte[4];
        WriteBe32(crc, 0, unchecked((int)Crc32(crcInput)));
        s.Write(crc);
    }

    private static void WriteBe32(byte[] b, int offset, int v)
    {
        b[offset] = (byte)(v >> 24);
        b[offset + 1] = (byte)(v >> 16);
        b[offset + 2] = (byte)(v >> 8);
        b[offset + 3] = (byte)v;
    }

    private static uint Crc32(byte[] data)
    {
        var crc = 0xFFFFFFFFu;
        foreach (var bb in data)
        {
            crc ^= bb;
            for (var i = 0; i < 8; i++)
            {
                crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320u : crc >> 1;
            }
        }
        return crc ^ 0xFFFFFFFFu;
    }

    // ---------------- 白图防御 ----------------

    [Fact]
    public void LooksLikeBlankOrWhite_Detects_White_And_Transparent_Rejects_Real_Icon()
    {
        // 纯白不透明 → 拦截
        Assert.True(IconDiskCacheService.LooksLikeBlankOrWhite(PngBytes(4, 4, 255, 255, 255, 255)));
        // 全透明(alpha=0)→ 空图,拦截
        Assert.True(IconDiskCacheService.LooksLikeBlankOrWhite(PngBytes(4, 4, 255, 255, 255, 0)));
        // 纯黑不透明(实测远端原图形态)→ 放行
        Assert.False(IconDiskCacheService.LooksLikeBlankOrWhite(PngBytes(4, 4, 0, 0, 0, 255)));
        // 近白但含深色像素(正常图标:白底 + 深色轮廓)→ 放行
        static (byte R, byte G, byte B, byte A) Outline(int x, int y)
            => (x == 0 || y == 0)
                ? (R: (byte)20, G: (byte)20, B: (byte)20, A: (byte)255)   // 白色形状/底板上的深色描边
                : (R: (byte)255, G: (byte)255, B: (byte)255, A: (byte)255);
        var iconWithOutline = PngBytes(8, 8, Outline);
        Assert.False(IconDiskCacheService.LooksLikeBlankOrWhite(iconWithOutline));

        // 非 PNG / 空数据 / 损坏数据 → 不拦截(避免误杀)
        Assert.False(IconDiskCacheService.LooksLikeBlankOrWhite(null));
        Assert.False(IconDiskCacheService.LooksLikeBlankOrWhite([]));
        Assert.False(IconDiskCacheService.LooksLikeBlankOrWhite(Encoding.UTF8.GetBytes("fake-image-role")));
        var truncated = PngBytes(4, 4, 255, 255, 255, 255)[..20]; // 截断的 PNG
        Assert.False(IconDiskCacheService.LooksLikeBlankOrWhite(truncated));
    }

    [Fact]
    public void LooksLikeBlankOrWhite_Handles_Palette_Indexed_Png_With_TRNS()
    {
        // 实测共鸣链白图里有一批是索引色(bitDepth=8 / colorType=3 + tRNS 透明索引),
        // 必须能解析调色板与 tRNS 才能判定「白形状 + 透明背景」为纯白图。
        var whiteOnTransparent = PalettePng(4, 4, (255, 255, 255), (0, 0, 0));
        Assert.True(IconDiskCacheService.LooksLikeBlankOrWhite(whiteOnTransparent));

        // 索引色 + 深色前景 → 放行
        var darkOnTransparent = PalettePng(4, 4, (20, 20, 20), (0, 0, 0));
        Assert.False(IconDiskCacheService.LooksLikeBlankOrWhite(darkOnTransparent));
    }

    [Fact]
    public void LooksLikeBlankOrWhite_Handles_1Bit_And_Sixteen_Bit_Png()
    {
        // 1 位索引色(bitDepth=1, colorType=3):白色前景 + 透明索引 → 纯白图
        Assert.True(IconDiskCacheService.LooksLikeBlankOrWhite(SubBytePng(8, 8, bitsPerPixel: 1)));
        // 16 位 RGBA 纯白 → 纯白图
        Assert.True(IconDiskCacheService.LooksLikeBlankOrWhite(Png16Bit(2, 2, white: true)));
        // 16 位 RGBA 深色 → 放行
        Assert.False(IconDiskCacheService.LooksLikeBlankOrWhite(Png16Bit(2, 2, white: false)));
    }

    /// <summary>构造 bitDepth=8 / colorType=3 索引色 PNG:foreground 为不透明调色板项,索引 0 经 tRNS 置为全透明。</summary>
    private static byte[] PalettePng(int width, int height, (byte R, byte G, byte B) fg, (byte R, byte G, byte B) bg)
    {
        // 调色板:[transparentIdx=0]=bg, [1]=fg
        byte[] palette = [bg.R, bg.G, bg.B, fg.R, fg.G, fg.B];
        byte[] trns = [0]; // 索引 0 全透明

        var stride = width; // 每像素 1 字节索引
        var raw = new byte[height * (stride + 1)];
        for (var y = 0; y < height; y++)
        {
            var row = y * (stride + 1);
            raw[row] = 0;
            for (var x = 0; x < width; x++)
            {
                // 棋盘:一半透明底、一半白色前景
                raw[row + 1 + x] = (byte)((x + y) % 2 == 0 ? 1 : 0);
            }
        }

        using var ms = new MemoryStream();
        ms.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
        var ihdr = new byte[13];
        WriteBe32(ihdr, 0, width);
        WriteBe32(ihdr, 4, height);
        ihdr[8] = 8; // bitDepth
        ihdr[9] = 3; // colorType = 调色板
        WriteChunk(ms, "IHDR", ihdr);
        WriteChunk(ms, "PLTE", palette);
        WriteChunk(ms, "tRNS", trns);
        using (var compressed = new MemoryStream())
        {
            using (var z = new System.IO.Compression.ZLibStream(compressed, System.IO.Compression.CompressionLevel.Optimal, leaveOpen: true))
            {
                z.Write(raw, 0, raw.Length);
            }
            WriteChunk(ms, "IDAT", compressed.ToArray());
        }
        WriteChunk(ms, "IEND", []);
        return ms.ToArray();
    }

    /// <summary>构造 bitDepth=1 / colorType=3 的索引色 PNG(白色前景索引 1 + tRNS 透明索引 0)。</summary>
    private static byte[] SubBytePng(int width, int height, int bitsPerPixel)
    {
        byte[] palette = [0, 0, 0, 255, 255, 255]; // 索引 0=黑(但被 tRNS 置透), 1=白
        byte[] trns = [0];

        var stride = (width * bitsPerPixel + 7) / 8;
        var raw = new byte[height * (stride + 1)];
        for (var y = 0; y < height; y++)
        {
            var row = y * (stride + 1);
            raw[row] = 0;
            for (var x = 0; x < width; x++)
            {
                // 棋盘:交替索引 1 / 0(1 位 = 8 像素/字节)
                var bitIndex = x * bitsPerPixel;
                var value = ((x + y) % 2 == 0) ? 1 : 0;
                raw[row + 1 + (bitIndex >> 3)] |= (byte)(value << (8 - bitsPerPixel - (bitIndex & 7)));
            }
        }

        using var ms = new MemoryStream();
        ms.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
        var ihdr = new byte[13];
        WriteBe32(ihdr, 0, width);
        WriteBe32(ihdr, 4, height);
        ihdr[8] = (byte)bitsPerPixel;
        ihdr[9] = 3;
        WriteChunk(ms, "IHDR", ihdr);
        WriteChunk(ms, "PLTE", palette);
        WriteChunk(ms, "tRNS", trns);
        using (var compressed = new MemoryStream())
        {
            using (var z = new System.IO.Compression.ZLibStream(compressed, System.IO.Compression.CompressionLevel.Optimal, leaveOpen: true))
            {
                z.Write(raw, 0, raw.Length);
            }
            WriteChunk(ms, "IDAT", compressed.ToArray());
        }
        WriteChunk(ms, "IEND", []);
        return ms.ToArray();
    }

    /// <summary>构造 16 位 RGBA PNG(RGB 取高字节判定;alpha 恒为不透明)。</summary>
    private static byte[] Png16Bit(int width, int height, bool white)
    {
        var hi = white ? (byte)255 : (byte)0;
        var stride = width * 4 * 2; // 4 通道 × 2 字节
        var raw = new byte[height * (stride + 1)];
        for (var y = 0; y < height; y++)
        {
            var row = y * (stride + 1);
            raw[row] = 0;
            for (var x = 0; x < width; x++)
            {
                var p = row + 1 + x * 8;
                raw[p] = hi; raw[p + 1] = hi;         // R
                raw[p + 2] = hi; raw[p + 3] = hi;     // G
                raw[p + 4] = hi; raw[p + 5] = hi;     // B
                raw[p + 6] = 255; raw[p + 7] = 255;   // A = 不透明
            }
        }

        using var ms = new MemoryStream();
        ms.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
        var ihdr = new byte[13];
        WriteBe32(ihdr, 0, width);
        WriteBe32(ihdr, 4, height);
        ihdr[8] = 16; // bitDepth
        ihdr[9] = 6;  // RGBA
        WriteChunk(ms, "IHDR", ihdr);
        using (var compressed = new MemoryStream())
        {
            using (var z = new System.IO.Compression.ZLibStream(compressed, System.IO.Compression.CompressionLevel.Optimal, leaveOpen: true))
            {
                z.Write(raw, 0, raw.Length);
            }
            WriteChunk(ms, "IDAT", compressed.ToArray());
        }
        WriteChunk(ms, "IEND", []);
        return ms.ToArray();
    }

    [Fact]
    public async Task Store_Rejects_White_Png_But_Keeps_Real_Icon()
    {
        var white = PngBytes(2, 2, 255, 255, 255, 255);
        var black = PngBytes(2, 2, 0, 0, 0, 255);
        var service = new IconDiskCacheService(_dir, (_, _) => Task.FromResult<byte[]?>(white));

        await service.CacheUrlAsync(IconDiskCacheService.CategoryRole, "白图", "https://img/white.png");
        Assert.Null(service.GetCachedIconPath(IconDiskCacheService.CategoryRole, "白图")); // 未落盘
        Assert.False(File.Exists(Path.Combine(_dir, IconDiskCacheService.CategoryRole, "白图.png")));

        var service2 = new IconDiskCacheService(_dir, (_, _) => Task.FromResult<byte[]?>(black));
        await service2.CacheUrlAsync(IconDiskCacheService.CategoryRole, "黑图", "https://img/black.png");
        var path = service2.GetCachedIconPath(IconDiskCacheService.CategoryRole, "黑图");
        Assert.NotNull(path);
        Assert.True(File.Exists(path));
    }

    // ---------------- 共鸣链不再使用本地磁盘缓存替换 ----------------

    [Fact]
    public async Task ResolveIcon_Chain_Always_Keeps_Remote_Url_Even_When_Cached()
    {
        // 刻意用「非 PNG」字节绕过白图防御,确认 chain 仍然不走本地缓存(防御之外的第二道保险)
        var service = new IconDiskCacheService(_dir, (_, _) => Task.FromResult<byte[]?>(Encoding.UTF8.GetBytes("chain-icon")));
        await service.CacheUrlAsync(IconDiskCacheService.CategoryChain, "一链", "https://img.kurobbs.com/chain1.png");

        // 磁盘缓存确实存在(未破坏既有落盘能力)…
        var cached = service.GetCachedIconPath(IconDiskCacheService.CategoryChain, "一链");
        Assert.NotNull(cached);
        // …但 ResolveIcon 对 chain 一律返回远端 URL(白图污染修复的核心)
        Assert.Equal("https://guide-res.aki-game.com/chain1.png",
            service.ResolveIcon(IconDiskCacheService.CategoryChain, "一链", "https://guide-res.aki-game.com/chain1.png"));

        // 其他分类不受影响:仍按名称命中本地缓存
        await service.CacheUrlAsync(IconDiskCacheService.CategoryWeapon, "千古洑流", "https://img.kurobbs.com/w.png");
        var resolvedWeapon = service.ResolveIcon(IconDiskCacheService.CategoryWeapon, "千古洑流", "https://guide-res/w.png");
        Assert.NotEqual("https://guide-res/w.png", resolvedWeapon);
        Assert.True(File.Exists(resolvedWeapon));
    }

    [Fact]
    public void ResolveIcon_Chain_Falls_Back_When_Not_Cached()
    {
        var service = new IconDiskCacheService(_dir);
        const string url = "https://guide-res.aki-game.com/chain2.png";
        Assert.Equal(url, service.ResolveIcon(IconDiskCacheService.CategoryChain, "二链", url));
    }

    [Fact]
    public async Task CacheRoleIconsAsync_Does_Not_Predownload_Chain_Icons()
    {
        // 预下载枚举与 ResolveIcon 的 chain 绕过策略对齐:chain URL 一次都不该被请求
        var requested = new List<string>();
        var service = new IconDiskCacheService(_dir, (url, _) =>
        {
            requested.Add(url);
            return Task.FromResult<byte[]?>(Encoding.UTF8.GetBytes("icon"));
        });

        await service.CacheRoleIconsAsync(new RoleDetail
        {
            Role = new RoleInfo { RoleName = "莫宁", RolePicUrl = "https://img.kurobbs.com/role.png" },
            Chains =
            [
                new ChainInfo { ChainName = "一链", IconUrl = "https://img.kurobbs.com/chain1.png" },
                new ChainInfo { ChainName = "二链", IconUrl = "https://img.kurobbs.com/chain2.png" },
            ],
        });

        // 只下载了角色立绘:两条 chain 图标连请求都没发出
        var only = Assert.Single(requested);
        Assert.Equal("https://img.kurobbs.com/role.png", only);
        Assert.Null(service.GetCachedIconPath(IconDiskCacheService.CategoryChain, "一链"));
        Assert.False(Directory.Exists(Path.Combine(_dir, IconDiskCacheService.CategoryChain)));
    }

    // ---------------- 污染缓存清理 ----------------

    [Fact]
    public async Task PurgeCategory_Removes_Files_And_Index_Entries_Only_For_That_Category()
    {
        var service = new IconDiskCacheService(_dir, (_, _) => Task.FromResult<byte[]?>(Encoding.UTF8.GetBytes("img")));
        await service.CacheUrlAsync(IconDiskCacheService.CategoryChain, "一链", "https://img/chain1.png");
        await service.CacheUrlAsync(IconDiskCacheService.CategoryChain, "二链", "https://img/chain2.png");
        await service.CacheUrlAsync(IconDiskCacheService.CategoryWeapon, "千古洑流", "https://img/w.png");

        var removed = service.PurgeCategory(IconDiskCacheService.CategoryChain);

        Assert.Equal(2, removed);
        Assert.Null(service.GetCachedIconPath(IconDiskCacheService.CategoryChain, "一链"));
        Assert.Null(service.GetCachedIconPath(IconDiskCacheService.CategoryChain, "二链"));
        // 其他分类保留
        Assert.NotNull(service.GetCachedIconPath(IconDiskCacheService.CategoryWeapon, "千古洑流"));
        Assert.False(Directory.Exists(Path.Combine(_dir, IconDiskCacheService.CategoryChain)));

        // 清理结果持久化:新实例读到的是已清理后的索引
        var reloaded = new IconDiskCacheService(_dir);
        Assert.Null(reloaded.GetCachedIconPath(IconDiskCacheService.CategoryChain, "一链"));
        Assert.NotNull(reloaded.GetCachedIconPath(IconDiskCacheService.CategoryWeapon, "千古洑流"));
    }

    [Fact]
    public void PurgeCategory_Refuses_Unsafe_Names()
    {
        var service = new IconDiskCacheService(_dir);
        var outside = Path.Combine(Path.GetTempPath(), "mc_kuro_should_not_delete");
        Directory.CreateDirectory(outside);
        File.WriteAllBytes(Path.Combine(outside, "keep.png"), [1, 2, 3]);
        try
        {
            Assert.Equal(0, service.PurgeCategory(".."));
            Assert.Equal(0, service.PurgeCategory("../" + Path.GetFileName(outside)));
            Assert.Equal(0, service.PurgeCategory("..\\" + Path.GetFileName(outside)));
            Assert.Equal(0, service.PurgeCategory("a/b"));
            Assert.Equal(0, service.PurgeCategory(""));
            Assert.True(File.Exists(Path.Combine(outside, "keep.png"))); // 目录外文件未被删
        }
        finally
        {
            try { Directory.Delete(outside, recursive: true); } catch { /* 忽略 */ }
        }
    }
}
