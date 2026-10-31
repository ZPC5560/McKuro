using McKuro.Core.Services.Game;
using Xunit;

namespace McKuro.Tests;

/// <summary>图形组件(DLSS/XeSS)定位:H10 去掉了对整个游戏目录的三次递归搜索。</summary>
public class GraphicsComponentLocatorTests
{
    /// <summary>
    /// H10:不再全盘递归 —— 已知位置直接命中;不在已知位置的 DLL 由剪枝遍历找到;
    /// 资源目录(Content/Saved/DiffData…)里的同名文件不会被找到(说明这些目录确实被剪掉了)。
    /// </summary>
    [Fact]
    public async Task GraphicsComponents_Found_In_Known_And_Fallback_Locations_Only()
    {
        var dir = Path.Combine(Path.GetTempPath(), "mckuro-gfx-" + Guid.NewGuid().ToString("N"));
        void Touch(string relative)
        {
            var full = Path.Combine(dir, relative.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            File.WriteAllBytes(full, [0x4D, 0x5A]); // 不是有效 PE:版本号读出 0.0.0.0,但"找到了"
        }
        try
        {
            Touch("Engine/Plugins/Runtime/Nvidia/StreamlineCore/Binaries/ThirdParty/Win64/nvngx_dlssg.dll");
            Touch("Client/Plugins/Vendor/SomeDlss/Win64/nvngx_dlss.dll");
            Touch("Client/Content/Paks/libxess.dll");
            Touch("DiffData/3.7.0/libxess.dll");

            var paths = new GamePathResolver(() => dir);
            var updater = new GameUpdater(null!, null!, null!, paths, Path.GetTempPath());
            var versions = await updater.GetLocalGraphicsComponentVersionsAsync();

            Assert.Equal(3, versions.Count);
            Assert.NotEqual("未找到文件", versions.Single(v => v.DisplayName == "DLSS").Version);
            Assert.NotEqual("未找到文件", versions.Single(v => v.DisplayName == "DLSS 帧生成").Version);
            Assert.Equal("未找到文件", versions.Single(v => v.DisplayName == "XeSS").Version);

            // 第二次读走缓存,结果一致
            Assert.Equal(
                versions.Select(v => v.Version),
                updater.GetLocalGraphicsComponentVersions().Select(v => v.Version));
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { }
        }
    }
}
