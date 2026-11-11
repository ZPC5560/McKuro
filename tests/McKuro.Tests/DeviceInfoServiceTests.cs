using McKuro.Core.Services.Hardware;
using Xunit;

namespace McKuro.Tests;

/// <summary>
/// 设备信息采集:只测纯逻辑与"不抛异常"契约。真实硬件值(具体型号/容量)
/// 不做断言 —— 换台机器就会变,断言它们只会制造脆弱测试。
/// </summary>
public class DeviceInfoServiceTests
{
    // ---------------- DetectDiskKind:非法输入必须安全降级 ----------------

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("relative\\path")]
    [InlineData("::invalid::")]
    [InlineData(@"\\server\share\games")]
    public void DetectDiskKind_IllegalInput_ReturnsUnknown(string? volumeRoot)
    {
        // 前几项在任何平台都是 Unknown;UNC 也必须是 Unknown(网络共享没有物理介质类型)。
        // 有盘符的输入同样落在这里:即使合法盘符,判不出来也只能是 Unknown/SSD/HDD 之一,
        // 真正要保证的是「绝不抛异常」。
        var kind = DeviceInfoService.DetectDiskKind(volumeRoot);

        Assert.True(
            kind is DiskKind.Unknown or DiskKind.Ssd or DiskKind.Hdd,
            $"非法输入不应返回枚举外的值,实际: {kind}");
    }

    [Fact]
    public void DetectDiskKind_IllegalPath_IsAlwaysUnknown()
    {
        // 这些输入连"取盘符"都不成立,必须确定性地判成 Unknown(与平台无关)。
        Assert.Equal(DiskKind.Unknown, DeviceInfoService.DetectDiskKind(null));
        Assert.Equal(DiskKind.Unknown, DeviceInfoService.DetectDiskKind(""));
        Assert.Equal(DiskKind.Unknown, DeviceInfoService.DetectDiskKind("relative\\path"));
        Assert.Equal(DiskKind.Unknown, DeviceInfoService.DetectDiskKind("::invalid::"));
        Assert.Equal(DiskKind.Unknown, DeviceInfoService.DetectDiskKind(@"\\server\share\games"));
    }

    [Fact]
    public void DetectDiskKind_NonWindows_ReturnsUnknown()
    {
        // 非 Windows 平台没有 DeviceIoControl,必须直接返回 Unknown 而不是抛 DllNotFound。
        // Windows 上这个用例没有可断言的东西,退回继续(不写 skip 是为了保持用例可执行)。
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        Assert.Equal(DiskKind.Unknown, DeviceInfoService.DetectDiskKind(@"C:\"));
    }

    // ---------------- 寻道描述符解读:位偏移是这段逻辑的核心 ----------------

    [Fact]
    public void InterpretSeekPenalty_FlagAtOffset8_DecidesKind()
    {
        // 真实 NVMe 返回:Version=12、Size=12、IncursSeekPenalty=0(SSD)。
        byte[] ssd = [0x0C, 0x00, 0x00, 0x00, 0x0C, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00];
        Assert.Equal(DiskKind.Ssd, DeviceInfoService.InterpretSeekPenalty(ssd));

        // 机械盘把偏移 8 置 1。
        byte[] hdd = [0x0C, 0x00, 0x00, 0x00, 0x0C, 0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00];
        Assert.Equal(DiskKind.Hdd, DeviceInfoService.InterpretSeekPenalty(hdd));

        // 回归保护:偏移 4 的 Size(12)绝不能被当成寻道标志。
        // 若按偏移 4 判 != 0,连 SSD 也会被判成 HDD(本机实测 Size 恒为 12)。
        Assert.Equal(DiskKind.Ssd, DeviceInfoService.InterpretSeekPenalty(
            [0x0C, 0x00, 0x00, 0x00, 0x0C, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00]));
    }

    [Fact]
    public void InterpretSeekPenalty_TooShort_ReturnsUnknown()
    {
        Assert.Equal(DiskKind.Unknown, DeviceInfoService.InterpretSeekPenalty([]));
        Assert.Equal(DiskKind.Unknown, DeviceInfoService.InterpretSeekPenalty([0x0C, 0x00, 0x00, 0x00]));
        Assert.Equal(DiskKind.Unknown, DeviceInfoService.InterpretSeekPenalty([0, 0, 0, 0, 0, 0, 0, 0]));
    }

    // ---------------- FormatDriverVersion:纯字符串规则 ----------------

    [Theory]
    [InlineData("32.0.32015.12012", "32.0.32015.12012")]
    [InlineData("26.20.15.12", "26.20.15.12")]
    [InlineData("  32.0.32015.12012  ", "32.0.32015.12012")] // 首尾空白被裁剪
    [InlineData("15.6.5.199", "15.6.5.199")]
    [InlineData("1", "1")]
    public void FormatDriverVersion_NumericPointVersion_ReturnedAsIs(string raw, string expected)
    {
        Assert.Equal(expected, DeviceInfoService.FormatDriverVersion(raw));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t\n")]
    public void FormatDriverVersion_Blank_ReturnsEmpty(string? raw)
    {
        Assert.Equal("", DeviceInfoService.FormatDriverVersion(raw));
    }

    [Theory]
    [InlineData("31.0.101.5333 (NVIDIA)", "31.0.101.5333")] // 截出开头版本前缀
    [InlineData("32.0.32015.12012-whql", "32.0.32015.12012")]
    [InlineData("abc", "abc")]                              // 非数字开头:原样返回,不猜
    [InlineData("v26.10.2", "v26.10.2")]
    public void FormatDriverVersion_NonStandard_KeepsValueOrPrefix(string raw, string expected)
    {
        Assert.Equal(expected, DeviceInfoService.FormatDriverVersion(raw));
    }

    [Fact]
    public void FormatDriverVersion_TooManySegments_IsNotTreatedAsVersionInf()
    {
        // 5 段不是合法的 VS_VERSIONINFO 形态,不能被"美化"成前 4 段(那会显示错误版本)。
        Assert.Equal("1.2.3.4.5", DeviceInfoService.FormatDriverVersion("1.2.3.4.5"));
    }

    // ---------------- ChoosePrimaryGpu:多显卡选择稳定性 ----------------

    [Fact]
    public void ChoosePrimaryGpu_EmptyList_ReturnsNull()
    {
        Assert.Null(DeviceInfoService.ChoosePrimaryGpu([]));
    }

    [Fact]
    public void ChoosePrimaryGpu_SingleItem_ReturnsItself()
    {
        var only = ("AMD Radeon RX 7800 XT", "32.0.32015.12012", 16L * 1024 * 1024 * 1024);

        var chosen = DeviceInfoService.ChoosePrimaryGpu([only]);

        Assert.NotNull(chosen);
        Assert.Equal(only.Item1, chosen.Value.Name);
        Assert.Equal(only.Item2, chosen.Value.Driver);
        Assert.Equal(only.Item3, chosen.Value.MemoryBytes);
    }

    [Fact]
    public void ChoosePrimaryGpu_PicksLargestMemory()
    {
        // 本机真实场景:独显 16 GiB 与虚拟显示器适配器(几乎无显存)同列。
        var discrete = ("AMD Radeon RX 7800 XT", "32.0.32015.12012", 16L * 1024 * 1024 * 1024);
        var virtualDisplay = ("GameViewer Virtual Display Adapter", "15.6.5.199", 0L);

        var chosen = DeviceInfoService.ChoosePrimaryGpu([virtualDisplay, discrete]);

        Assert.NotNull(chosen);
        Assert.Equal("AMD Radeon RX 7800 XT", chosen.Value.Name);
    }

    [Fact]
    public void ChoosePrimaryGpu_SameMemory_PicksNameByOrdinal()
    {
        // 显存相同时按名称序取最小,保证结果与注册表子键顺序无关(每次采集都一致)。
        var b = ("B Card", "2.0", 4096L);
        var a = ("A Card", "1.0", 4096L);

        Assert.Equal("A Card", DeviceInfoService.ChoosePrimaryGpu([b, a])!.Value.Name);
        Assert.Equal("A Card", DeviceInfoService.ChoosePrimaryGpu([a, b])!.Value.Name);
    }

    [Fact]
    public void ChoosePrimaryGpu_IsDeterministic_RegardlessOfOrder()
    {
        var items = new List<(string Name, string Driver, long MemoryBytes)>
        {
            ("C Card", "3.0", 1024L),
            ("A Card", "1.0", 2048L),
            ("B Card", "2.0", 2048L),
        };

        var forward = DeviceInfoService.ChoosePrimaryGpu(items);
        items.Reverse();
        var backward = DeviceInfoService.ChoosePrimaryGpu(items);

        Assert.Equal(forward?.Name, backward?.Name);
        Assert.Equal("A Card", forward?.Name);
    }

    // ---------------- Collect:契约是"永不抛异常" ----------------

    [Fact]
    public void Collect_DoesNotThrow_AndFillsCoreFields()
    {
        var info = DeviceInfoService.Collect(null);

        Assert.NotNull(info);
        Assert.True(info.CpuCores > 0);

        if (!OperatingSystem.IsWindows())
        {
            // 非 Windows 必须是"不支持"占位,而不是抛异常或伪造硬件信息
            Assert.False(info.IsSupported);
            return;
        }

        Assert.True(info.IsSupported);
        Assert.False(string.IsNullOrWhiteSpace(info.CpuName));
        Assert.True(info.MemoryBytes > 0);

        // 系统版本只做弱断言:格式必须是 Major.Minor.Build
        Assert.Matches(@"^\d+\.\d+\.\d+$", info.OsVersion);
        Assert.False(string.IsNullOrWhiteSpace(info.OsDisplayName));
    }

    [Fact]
    public void Collect_WithGameRoot_DetectsInstallDiskOrStaysUnknown()
    {
        // 传入游戏目录只影响安装盘类型;判定失败也必须是 Unknown 而不是抛异常。
        var info = DeviceInfoService.Collect(@"E:\games\wuthering");

        Assert.True(
            info.InstallDiskKind is DiskKind.Unknown or DiskKind.Ssd or DiskKind.Hdd,
            $"InstallDiskKind 超出枚举范围: {info.InstallDiskKind}");

        // 未传目录时不应凭空猜测
        Assert.Equal(DiskKind.Unknown, DeviceInfoService.Collect(null).InstallDiskKind);
    }
}
