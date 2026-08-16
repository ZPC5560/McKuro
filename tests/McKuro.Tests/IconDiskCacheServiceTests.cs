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
        var chainPath = service.GetCachedIconPath(IconDiskCacheService.CategoryChain, "一链");
        var attrPath = service.GetCachedIconPath(IconDiskCacheService.CategoryAttr, "暴击");
        var echoPath = service.GetCachedIconPath(IconDiskCacheService.CategoryEcho, "啸谷幼猿");
        Assert.NotNull(rolePath);
        Assert.NotNull(weaponPath);
        Assert.NotNull(skillPath);
        Assert.NotNull(chainPath);
        Assert.NotNull(attrPath);
        Assert.NotNull(echoPath);
        foreach (var p in new[] { rolePath, weaponPath, skillPath, chainPath, attrPath, echoPath })
        {
            Assert.True(File.Exists(p));
        }

        // 6 个 http 图标全部请求,且 ResolveIcon 命中本地路径
        Assert.Equal(6, downloader.Requested.Count);
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

        // 非 http 的本地路径未请求;仅 5 个 http 图标被下载
        Assert.DoesNotContain(downloader.Requested, u => u.StartsWith(_dir, StringComparison.Ordinal));
        Assert.Contains("https://img.kurobbs.com/weapon.png", downloader.Requested);
        Assert.Equal(5, downloader.Requested.Count);

        // 再次缓存同角色:已缓存名称不重复下载
        await service.CacheRoleIconsAsync(role);
        Assert.Equal(5, downloader.Requested.Count);
    }

    [Fact]
    public async Task CacheRoleIconsAsync_Ignores_Download_Failure()
    {
        var downloader = new FakeDownloader(new Dictionary<string, byte[]>()); // 所有 URL 返回 null
        var service = new IconDiskCacheService(_dir, downloader.Download);

        await service.CacheRoleIconsAsync(BuildRole()); // 不应抛异常

        Assert.Equal(6, downloader.Requested.Count);
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
}
