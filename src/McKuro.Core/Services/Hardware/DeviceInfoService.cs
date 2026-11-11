using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32;

// 命名空间刻意避开 McKuro.Core.Services.System:一旦它存在,C# 在 Services.* 下的文件里
// 会优先把字面量 `System.Text...` 解析成 `McKuro.Core.Services.System.Text...`,
// 直接打断仓库里 19 个文件、62 处显式 `System.Xxx` 引用(如 [System.Text.RegularExpressions.GeneratedRegex])。
namespace McKuro.Core.Services.Hardware;

/// <summary>安装磁盘的物理介质类型。</summary>
public enum DiskKind
{
    /// <summary>未能判定:非 Windows、盘符非法、无权限,或设备/驱动不支持该查询。</summary>
    Unknown,

    /// <summary>固态盘(随机寻道无代价)。</summary>
    Ssd,

    /// <summary>机械盘(随机寻道有代价)。</summary>
    Hdd,
}

/// <summary>
/// 一台机器的硬件/系统画像(对齐官方启动器「游戏设置 → 设备信息」页的展示项)。
/// <para>
/// 全部字段都有安全默认值:任何一项采集失败只留空/0,调用方可以直接按值渲染,
/// 不需要判空(空串由展示层决定是否隐藏该行)。
/// </para>
/// </summary>
public sealed record DeviceInfo
{
    /// <summary>CPU 型号,例:"AMD Ryzen 7 9700X 8-Core Processor"。</summary>
    public string CpuName { get; init; } = "";

    /// <summary>逻辑处理器数(<see cref="Environment.ProcessorCount"/>)。</summary>
    public int CpuCores { get; init; }

    /// <summary>物理内存总量(字节)。</summary>
    public long MemoryBytes { get; init; }

    /// <summary>主显卡名称,例:"AMD Radeon RX 7800 XT"。</summary>
    public string GpuName { get; init; } = "";

    /// <summary>专用显存(字节)。读不到为 0。</summary>
    public long GpuMemoryBytes { get; init; }

    /// <summary>驱动版本(原始 VS_VERSIONINFO 数字),例:"32.0.32015.12012"。</summary>
    public string GpuDriverVersion { get; init; } = "";

    /// <summary>能读到厂商版本串时的美化文本,例:"AMD Software: Adrenalin Edition 26.10.2";读不到为空。</summary>
    public string GpuDriverEdition { get; init; } = "";

    /// <summary>游戏安装盘是 SSD 还是 HDD(未传入游戏目录或判定失败时为 <see cref="DiskKind.Unknown"/>)。</summary>
    public DiskKind InstallDiskKind { get; init; }

    /// <summary>系统版本号,取 Major.Minor.Build,例:"10.0.28120"。</summary>
    public string OsVersion { get; init; } = "";

    /// <summary>系统展示名,例:"Windows 11"。</summary>
    public string OsDisplayName { get; init; } = "";

    /// <summary>当前平台是否支持采集。非 Windows 恒为 false,此时其余字段只有占位值。</summary>
    public bool IsSupported { get; init; }
}

/// <summary>
/// 硬件/系统信息采集(Native AOT 兼容)。
/// <para>
/// <b>为什么不用 WMI:</b> 项目 <c>PublishAot=true</c>,<c>System.Management</c> 依赖 COM 与
/// 反射式类型转换,AOT 下不可用(且要额外拖一个 Windows 专属包)。这里全部走
/// 「注册表 + kernel32 P/Invoke」:都是静态代码路径,无反射、无 <c>Activator</c>、无 JSON。
/// </para>
/// <para>
/// <b>失败即留空:</b> 参考 <c>ResidueScanner</c> 的取舍 —— 设备信息是纯展示项,
/// 任何一项读不到都只影响那一行,绝不能让整页崩掉,所以每段采集各自 try/catch 并降级。
/// </para>
/// </summary>
public static class DeviceInfoService
{
    // ---- 注册表位置 ----
    private const string CpuRegistryKey = @"HARDWARE\DESCRIPTION\System\CentralProcessor\0";
    private const string CpuNameValue = "ProcessorNameString";

    /// <summary>显示适配器类 GUID 下的实例键(0000/0001…)才是每张显卡的驱动信息。</summary>
    private const string GpuClassKey = @"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}";
    private const string GpuDriverDescValue = "DriverDesc";
    private const string GpuDriverVersionValue = "DriverVersion";

    // 注意:这两个是显卡实例键下的「带点的值名」,不是子键。
    private const string GpuMemoryQwordValue = "HardwareInformation.qwMemorySize";
    private const string GpuMemoryDwordValue = "HardwareInformation.MemorySize";

    /// <summary>AMD 安装器维护的驱动状态页(含 Edition 与当前安装版本)。</summary>
    private const string AmdDriverStateKey = @"SOFTWARE\AMD\AMDInstallManager\CheckForUpdates\GraphicsDriverConsumer";

    /// <summary>
    /// 显存上限合理性阈值(512 GiB)。超过它说明 DWORD 里存的不是 MB —— 实测 AMD 写的是
    /// 字节数的低 32 位,按 MB 换算会得到 4 EB,这种值必须丢弃而不是显示出来。
    /// </summary>
    private const long MaxPlausibleVramBytes = 512L * 1024 * 1024 * 1024;

    // ---- IOCTL_STORAGE_QUERY_PROPERTY ----
    private const uint IoctlStorageQueryProperty = 0x2D1400;

    /// <summary>STORAGE_PROPERTY_ID 中的 StorageDeviceSeekPenaltyProperty。</summary>
    private const int StorageDeviceSeekPenaltyProperty = 7;

    /// <summary>DEVICE_SEEK_PENALTY_DESCRIPTOR 的定长部分:Version(4) + Size(4) + Flag(1) + 对齐填充(3)。</summary>
    private const int SeekPenaltyDescriptorLength = 12;

    /// <summary>
    /// 寻道代价标志的字节偏移。<b>必须是 8</b> —— 偏移 0 是 Version、偏移 4 是 Size(结构体长度);
    /// 本机对 15 种查询属性实测「偏移 4 == 返回字节数」恒成立(寻道查询为 12),
    /// 若按偏移 4 判 <c>!= 0</c>,每块盘(含 NVMe)都会被误判成机械盘。
    /// </summary>
    private const int SeekPenaltyFlagOffset = 8;

    private const uint FileShareReadWrite = 3;   // FILE_SHARE_READ | FILE_SHARE_WRITE
    private const uint OpenExisting = 3;

    private static readonly IntPtr InvalidHandleValue = new(-1);

    /// <summary>采集本机硬件/系统信息。任何一项失败都只留空,不抛异常。</summary>
    /// <param name="gameRootDir">
    /// 游戏安装目录(或任意卷内路径),用于判定安装盘是 SSD 还是 HDD;
    /// 传 null 时 <see cref="DeviceInfo.InstallDiskKind"/> 为 <see cref="DiskKind.Unknown"/>。
    /// </param>
    public static DeviceInfo Collect(string? gameRootDir = null)
    {
        var systemVersion = Environment.OSVersion.Version;
        var osVersionText = $"{systemVersion.Major}.{systemVersion.Minor}.{systemVersion.Build}";

        if (!OperatingSystem.IsWindows())
        {
            // 非 Windows 只给「不支持」占位:设备信息页据此整页隐藏。
            // 这里必须正常返回而不是抛异常 —— 该 API 会被跨平台编译的 UI 代码无条件调用。
            return new DeviceInfo
            {
                CpuCores = Environment.ProcessorCount,
                OsVersion = osVersionText,
                IsSupported = false,
            };
        }

        var gpu = ReadPrimaryGpu();
        var gpuName = gpu?.Name ?? "";

        return new DeviceInfo
        {
            CpuName = ReadCpuName(),
            CpuCores = Environment.ProcessorCount,
            MemoryBytes = ReadTotalPhysicalMemory(),
            GpuName = gpuName,
            GpuMemoryBytes = gpu?.MemoryBytes ?? 0,
            GpuDriverVersion = FormatDriverVersion(gpu?.Driver),
            // 没拿到显卡名就不必去猜厂商版本串,省一次注册表访问
            GpuDriverEdition = gpuName.Length == 0 ? "" : ReadGpuDriverEdition(gpuName),
            InstallDiskKind = DetectDiskKind(gameRootDir),
            OsVersion = osVersionText,
            OsDisplayName = DescribeWindowsName(systemVersion.Build),
            IsSupported = true,
        };
    }

    /// <summary>由卷根路径判定物理磁盘类型(SSD/HDD)。非 Windows / 无权限返回 Unknown。</summary>
    /// <param name="volumeRoot">任意卷内路径,内部只取盘符(例:<c>D:\games\wuthering</c> → <c>D:</c>)。</param>
    public static DiskKind DetectDiskKind(string? volumeRoot)
    {
        if (!OperatingSystem.IsWindows())
        {
            return DiskKind.Unknown;
        }

        try
        {
            // 只认盘符根:UNC 网络路径(\\server\share)没有「物理介质类型」的概念,
            // 设备信息页显示"未知"比显示错误答案更诚实。
            var root = Path.GetPathRoot(volumeRoot ?? "");
            if (string.IsNullOrEmpty(root) || root.Length < 2 || root[1] != ':' || !char.IsAsciiLetter(root[0]))
            {
                return DiskKind.Unknown;
            }

            return QuerySeekPenalty(char.ToUpperInvariant(root[0]));
        }
        catch (Exception)
        {
            // 非法路径字符、权限不足、非 Windows 上的 DllNotFound 等统一按未知处理
            return DiskKind.Unknown;
        }
    }

    /// <summary>
    /// 解读 <c>IOCTL_STORAGE_QUERY_PROPERTY</c> +
    /// <c>StorageDeviceSeekPenaltyProperty</c> 返回的 <c>DEVICE_SEEK_PENALTY_DESCRIPTOR</c>。
    /// <para>
    /// 结构布局:<c>ULONG Version</c>(偏移 0)、<c>ULONG Size</c>(偏移 4)、
    /// <c>BOOLEAN IncursSeekPenalty</c>(偏移 8)。判据只取偏移 8:
    /// <c>IncursSeekPenalty == 0</c> 表示随机寻道无代价 → SSD。
    /// 偏移 4 是结构体长度(恒为 12),用它判会得出"全是机械盘"的错误结论。
    /// </para>
    /// <para>抽成纯函数是为了让这段位偏移逻辑能在没有真实硬件的情况下被单测覆盖。</para>
    /// </summary>
    public static DiskKind InterpretSeekPenalty(ReadOnlySpan<byte> descriptor)
    {
        if (descriptor.Length < SeekPenaltyDescriptorLength)
        {
            return DiskKind.Unknown;
        }

        return descriptor[SeekPenaltyFlagOffset] != 0 ? DiskKind.Hdd : DiskKind.Ssd;
    }

    /// <summary>
    /// 规范化驱动版本串(纯字符串处理,便于单测)。
    /// <para>
    /// 规则:空/null/全空白 → 空串;去掉首尾空白;本身就是「1~4 段纯数字点分」的版本
    /// (<c>32.0.32015.12012</c>、<c>26.20.15.12</c>)原样返回;若尾部带了厂商附注
    /// (<c>31.0.101.5333 (NVIDIA)</c>、<c>32.0.32015.12012-whql</c>)则截出开头的数字点分前缀;
    /// 其余形态一律原样返回 —— 展示层不该"猜"版本号,把非常规写法改坏比不美化更糟。
    /// </para>
    /// </summary>
    public static string FormatDriverVersion(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return "";
        }

        var trimmed = raw.Trim();
        if (IsNumericVersion(trimmed))
        {
            return trimmed;
        }

        var end = 0;
        while (end < trimmed.Length && (char.IsAsciiDigit(trimmed[end]) || trimmed[end] == '.'))
        {
            end++;
        }

        var prefix = trimmed[..end].TrimEnd('.');
        return IsNumericVersion(prefix) ? prefix : trimmed;
    }

    /// <summary>
    /// 多显卡里挑出「主显卡」:显存最大者优先;显存相同取名称序最小。
    /// <para>
    /// 为什么需要稳定规则:本机同时存在独显、核显/虚拟显示器适配器(<c>GameViewer Virtual Display Adapter</c>),
    /// 注册表子键顺序不保证跨机器/跨次一致,若按"第一个"取会报出虚拟显示器。
    /// 抽成纯函数后可以脱离真实硬件单测(与 <c>GamePathResolver</c> 的做法一致)。
    /// </para>
    /// </summary>
    /// <returns>空列表返回 null;否则返回**复制出**的选中条目。</returns>
    public static (string Name, string Driver, long MemoryBytes)? ChoosePrimaryGpu(
        IReadOnlyList<(string Name, string Driver, long MemoryBytes)> items)
    {
        if (items is null || items.Count == 0)
        {
            return null;
        }

        var best = items[0];
        for (var i = 1; i < items.Count; i++)
        {
            var candidate = items[i];
            if (candidate.MemoryBytes > best.MemoryBytes)
            {
                best = candidate;
            }
            else if (candidate.MemoryBytes == best.MemoryBytes
                     // Ordinal:不受区域设置影响,保证同一台机器每次采集结果相同
                     && string.CompareOrdinal(candidate.Name, best.Name) < 0)
            {
                best = candidate;
            }
        }

        return best;
    }

    // ------------------------------------------------------------------
    // 采集实现
    // ------------------------------------------------------------------

    /// <summary>CPU 型号。注册表里该值带尾部填充空格,必须 Trim。</summary>
    [SupportedOSPlatform("windows")]
    private static string ReadCpuName()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(CpuRegistryKey);
            return (key?.GetValue(CpuNameValue) as string)?.Trim() ?? "";
        }
        catch (Exception)
        {
            // 精简系统/权限受限时读不到,留空即可
            return "";
        }
    }

    /// <summary>物理内存总量。WMI 不可用,改走 GlobalMemoryStatusEx(内核直接给出,无 COM 依赖)。</summary>
    [SupportedOSPlatform("windows")]
    private static long ReadTotalPhysicalMemory()
    {
        try
        {
            var status = new MemoryStatusEx { Length = (uint)Marshal.SizeOf<MemoryStatusEx>() };
            return GlobalMemoryStatusEx(ref status) ? (long)status.TotalPhys : 0;
        }
        catch (Exception)
        {
            return 0;
        }
    }

    /// <summary>
    /// 枚举显示适配器类下的实例键,取显存最大的那张。
    /// 只保留 4 位数字编号的子键,并跳过没有 <c>DriverDesc</c> 的条目(同级的
    /// <c>Configuration</c>/<c>Properties</c> 不是显卡实例)。
    /// </summary>
    [SupportedOSPlatform("windows")]
    private static (string Name, string Driver, long MemoryBytes)? ReadPrimaryGpu()
    {
        try
        {
            using var classKey = Registry.LocalMachine.OpenSubKey(GpuClassKey);
            if (classKey is null)
            {
                return null;
            }

            var adapters = new List<(string Name, string Driver, long MemoryBytes)>();
            foreach (var subKeyName in classKey.GetSubKeyNames())
            {
                if (subKeyName.Length != 4 || !subKeyName.All(char.IsAsciiDigit))
                {
                    continue;
                }

                using var adapterKey = classKey.OpenSubKey(subKeyName);
                if (adapterKey is null)
                {
                    continue;
                }

                var name = (adapterKey.GetValue(GpuDriverDescValue) as string)?.Trim();
                if (string.IsNullOrEmpty(name))
                {
                    continue;
                }

                var driver = (adapterKey.GetValue(GpuDriverVersionValue) as string)?.Trim() ?? "";
                adapters.Add((name, driver, ReadGpuMemory(adapterKey)));
            }

            return ChoosePrimaryGpu(adapters);
        }
        catch (Exception)
        {
            // 注册表不可读时按"没有显卡信息"处理,不影响其余字段
            return null;
        }
    }

    /// <summary>
    /// 读专用显存(字节)。两种历史写法都要兼容:REG_QWORD 直接是 long,
    /// 少数驱动写成 8 字节小端 REG_BINARY。
    /// </summary>
    [SupportedOSPlatform("windows")]
    private static long ReadGpuMemory(RegistryKey adapterKey)
    {
        if (adapterKey.GetValue(GpuMemoryQwordValue) is { } packed)
        {
            var bytes = packed switch
            {
                long qword => qword,
                int dword => dword,
                byte[] binary when binary.Length >= sizeof(long) => BitConverter.ToInt64(binary, 0),
                _ => 0L,
            };
            if (bytes > 0)
            {
                return bytes;
            }
        }

        // 退回 DWORD(约定单位 MB)。实测 AMD 在该值里存的是字节数的低 32 位
        //(0xFFF00000),按 MB 换算会得到 4 EB —— 只接受合理区间内的值,
        // 否则宁可报 0 让 UI 显示"未知"。
        if (adapterKey.GetValue(GpuMemoryDwordValue) is int megabytes && megabytes > 0)
        {
            var bytes = (long)megabytes * 1024 * 1024;
            return bytes <= MaxPlausibleVramBytes ? bytes : 0;
        }

        return 0;
    }

    /// <summary>
    /// 厂商驱动的可读版本串(例:"AMD Software: Adrenalin Edition 26.10.2")。
    /// <para>
    /// 只取 AMD 的安装器状态页:<c>Edition</c> + <c>VersionCurrentlyInstalled</c>
    /// (后者比 <c>ExternalVersion</c> 更准 —— 实测前者 26.10.2、后者 26.10.1)。
    /// NVIDIA/Intel 没有等价的稳定键,靠注册表硬拼只会显示错版本,因此留空由 UI 隐藏该行。
    /// 整段放在 try/catch 里,失败静默。
    /// </para>
    /// </summary>
    [SupportedOSPlatform("windows")]
    private static string ReadGpuDriverEdition(string gpuName)
    {
        try
        {
            if (!gpuName.Contains("Radeon", StringComparison.OrdinalIgnoreCase)
                && !gpuName.Contains("AMD", StringComparison.OrdinalIgnoreCase))
            {
                return "";
            }

            using var key = Registry.LocalMachine.OpenSubKey(AmdDriverStateKey);
            if (key is null)
            {
                return "";
            }

            var edition = (key.GetValue("Edition") as string)?.Trim() ?? "";
            var installed = (key.GetValue("VersionCurrentlyInstalled") as string)?.Trim();
            var version = string.IsNullOrEmpty(installed)
                ? ((key.GetValue("ExternalVersion") as string)?.Trim() ?? "")
                : installed;

            if (edition.Length == 0)
            {
                return version;
            }

            return version.Length == 0 ? edition : $"{edition} {version}";
        }
        catch (Exception)
        {
            return "";
        }
    }

    /// <summary>
    /// 打开卷设备并查询「随机寻道是否有代价」。
    /// <para>
    /// 访问权限故意传 0:GENERIC_READ 在受限账户下会被卷设备直接拒绝,
    /// 而该 IOCTL 只需要句柄即可,传 0 反而让普通用户也能查。
    /// </para>
    /// </summary>
    [SupportedOSPlatform("windows")]
    private static DiskKind QuerySeekPenalty(char driveLetter)
    {
        var handle = CreateFileW($@"\\.\{driveLetter}:", 0, FileShareReadWrite, IntPtr.Zero, OpenExisting, 0, IntPtr.Zero);
        if (handle == InvalidHandleValue)
        {
            // 盘符不存在或无权打开设备(如某些受管控系统)
            return DiskKind.Unknown;
        }

        try
        {
            // STORAGE_PROPERTY_QUERY: PropertyId(4) + QueryType(4) + AdditionalParameters(8)。
            // 这里只发标准查询(QueryType=PropertyStandardQuery),后 8 字节保持全 0。
            var query = new byte[12];
            _ = BitConverter.TryWriteBytes(query.AsSpan(0, sizeof(int)), StorageDeviceSeekPenaltyProperty);

            // 描述符只有 12 字节,给 64 留余量(不同 Windows 版本返回长度略有差异)
            var descriptor = new byte[64];
            if (!DeviceIoControl(
                    handle,
                    IoctlStorageQueryProperty,
                    query,
                    (uint)query.Length,
                    descriptor,
                    (uint)descriptor.Length,
                    out var returned,
                    IntPtr.Zero))
            {
                return DiskKind.Unknown;
            }

            var available = (int)Math.Min(returned, (uint)descriptor.Length);
            return InterpretSeekPenalty(descriptor.AsSpan(0, available));
        }
        finally
        {
            _ = CloseHandle(handle);
        }
    }

    /// <summary>把 Build 号映射成人们口头称呼的系统名(11/10 的分界是 22000)。</summary>
    private static string DescribeWindowsName(int build) => build switch
    {
        >= 22000 => "Windows 11",
        >= 10240 => "Windows 10",
        _ => "Windows",
    };

    /// <summary>已规范化的版本串判定:1~4 段、每段非空且全为 ASCII 数字。</summary>
    private static bool IsNumericVersion(string text)
    {
        if (text.Length == 0)
        {
            return false;
        }

        var segments = text.Split('.');
        if (segments.Length > 4)
        {
            return false;
        }

        foreach (var segment in segments)
        {
            if (segment.Length == 0)
            {
                return false;
            }

            foreach (var c in segment)
            {
                if (!char.IsAsciiDigit(c))
                {
                    return false;
                }
            }
        }

        return true;
    }

    // ------------------------------------------------------------------
    // P/Invoke(Native AOT:全部静态声明,无反射、无 WMI)
    // ------------------------------------------------------------------

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatusEx
    {
        public uint Length;
        public uint MemoryLoad;
        public ulong TotalPhys;
        public ulong AvailPhys;
        public ulong TotalPageFile;
        public ulong AvailPageFile;
        public ulong TotalVirtual;
        public ulong AvailVirtual;
        public ulong AvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx buffer);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    private static extern IntPtr CreateFileW(
        string fileName,
        uint desiredAccess,
        uint shareMode,
        IntPtr securityAttributes,
        uint creationDisposition,
        uint flagsAndAttributes,
        IntPtr templateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeviceIoControl(
        IntPtr device,
        uint ioControlCode,
        byte[] inBuffer,
        uint inBufferSize,
        [Out] byte[] outBuffer,
        uint outBufferSize,
        out uint bytesReturned,
        IntPtr overlapped);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr handle);
}
