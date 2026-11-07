using System.Runtime;
using System.Runtime.InteropServices;

namespace McKuro.Services;

/// <summary>
/// 把工作集还给操作系统。
/// <para>
/// <b>为什么需要它:</b>托管堆通常只有几十 MB,而游戏启动时进程的工作集里还压着
/// 大量「已不再使用、但页仍驻留」的原生内存:mpv 解码帧池、ANGLE/GL 的临时面、
/// Skia 字形与图层缓存、以及已经 Free 过的堆页。GC 只回收托管对象、不会把页还给系统,
/// 于是 16 GB 机器上「游戏本体 + 启动器常驻 800 MB」就容易撞到内存不足。
/// </para>
/// <para>
/// <see cref="EmptyWorkingSet"/> 会把可回收的驻留页裁掉(下次访问由页错误换回,不影响正确性),
/// 配合一次压缩型 GC 才能同时收掉「托管已释放但页还留着」的部分。
/// 只在明确的让位时机调用(游戏会话开始/最小化),不能周期性调用 —— 会造成反复换页。
/// </para>
/// </summary>
public static class WorkingSetTrimmer
{
    [DllImport("psapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EmptyWorkingSet(IntPtr hProcess);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetCurrentProcess();

    /// <summary>
    /// 依次执行:压缩型 GC(含 LOH)→ 交还工作集。
    /// 必须在 UI 线程调用;内部只做一次 GC,避免与渲染线程抢锁。
    /// </summary>
    /// <returns>是否成功交还工作集(false = 平台不支持或调用失败,可忽略)。</returns>
    public static bool Trim()
    {
        try
        {
            // 先收托管:LOH 压缩能真正归还大对象页(图片缓冲/大数组常落在 LOH)
            GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive, blocking: true, compacting: true);
            GC.WaitForPendingFinalizers();
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive, blocking: true, compacting: true);
        }
        catch (Exception)
        {
            // GC 失败不影响后续交还工作集
        }

        if (OperatingSystem.IsWindows())
        {
            try
            {
                return EmptyWorkingSet(GetCurrentProcess());
            }
            catch (Exception)
            {
                return false;
            }
        }

        // macOS/Linux:无对应 API,压缩 GC 已足够;不引入平台特定的 madvise 调用。
        return false;
    }
}
