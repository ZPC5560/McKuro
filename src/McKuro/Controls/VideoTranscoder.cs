using System.Collections.Concurrent;
using HanumanInstitute.LibMpv;
using HanumanInstitute.LibMpv.Core;
using McKuro.Services;

namespace McKuro.Controls;

/// <summary>
/// 背景视频降分辨率转码:把缓存里超过档位上限的原片(官方宣传片是 3840x2280)用 libmpv 编码模式
/// 转成一份 <see cref="VideoCache.TranscodedPathFor"/> 副本,下次播放直接用它。
/// <para>
/// 为什么要转:播放侧的 vf 缩放只能缩小输出,硬解回传那一段始终按源分辨率分配。实测同一支宣传片
/// 4K 原片播放约 395 MB 内存 / 540 MB 显存,1080p 副本约 296 MB / 249 MB,画面看不出差别。
/// 12 秒 4K 片 libx264 veryfast 转码约 1.4 秒(16 核)。
/// </para>
/// <para>
/// 只在后台进行、一次一个、失败不影响播放(继续用原片)。本次会话播的仍是原片,
/// 下次会话(重进页面、挂起恢复、重启)才切到副本 —— 播放中途换文件会跳帧/从头播,不做。
/// </para>
/// </summary>
internal static class VideoTranscoder
{
    /// <summary>串行转码:编码本身已多线程,并发只会和播放、游戏抢 CPU。</summary>
    private static readonly SemaphoreSlim s_gate = new(1, 1);

    /// <summary>进行中的转码(按目标路径去重:多个控件同时播同一视频只转一次)。</summary>
    private static readonly ConcurrentDictionary<string, Task> s_running = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>本进程内转码失败或副本播放失败的目标路径:不再尝试/不再使用,下次启动再试。</summary>
    private static readonly ConcurrentDictionary<string, byte> s_bad = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>单次转码超时(按 <see cref="VideoCache.MaxTranscodeSourceBytes"/> 量级的源留足余量)。</summary>
    private static readonly TimeSpan Timeout = TimeSpan.FromMinutes(5);

    /// <summary>副本可用:文件完整存在且本进程内没有判过坏。</summary>
    public static bool IsUsable(string transcodedPath)
        => !s_bad.ContainsKey(transcodedPath) && VideoCache.IsUsable(transcodedPath);

    /// <summary>
    /// 副本播放失败:本进程内不再使用,并尝试删掉(mpv 可能还占着文件,稍后再删;删不掉留给清理)。
    /// 宁可误判多转一次,也不能让一份坏副本把视频永久变成静态图。
    /// </summary>
    public static void MarkBad(string transcodedPath)
    {
        if (!s_bad.TryAdd(transcodedPath, 0))
        {
            return;
        }
        System.Console.Error.WriteLine($"MCKURO-VIDEO 转码副本播放失败,停用并删除: {Path.GetFileName(transcodedPath)}");
        _ = Task.Run(async () =>
        {
            await Task.Delay(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
            VideoCache.TryDeleteFile(transcodedPath);
        });
    }

    /// <summary>后台排队转码(已有副本/进行中/本进程失败过则跳过)。不抛异常。</summary>
    public static void Enqueue(string sourcePath, string targetPath, int widthCap)
    {
        if (s_bad.ContainsKey(targetPath) || VideoCache.IsUsable(targetPath))
        {
            return;
        }
        s_running.GetOrAdd(targetPath, key => Task.Run(async () =>
        {
            try
            {
                await s_gate.WaitAsync().ConfigureAwait(false);
                try
                {
                    // 排队期间游戏可能已启动:让出 CPU,下次会话再转
                    if (AppServices.GameMonitor.State != GameSessionState.Idle || VideoCache.IsUsable(targetPath))
                    {
                        return;
                    }
                    if (!await TranscodeAsync(sourcePath, targetPath, widthCap).ConfigureAwait(false))
                    {
                        s_bad.TryAdd(targetPath, 0);
                    }
                }
                finally
                {
                    s_gate.Release();
                }
            }
            catch (Exception ex)
            {
                s_bad.TryAdd(targetPath, 0);
                System.Console.Error.WriteLine($"MCKURO-VIDEO 转码异常(继续用原片): {ex.Message}");
            }
            finally
            {
                s_running.TryRemove(key, out Task? _);
            }
        }));
    }

    /// <summary>编码到临时文件,完整结束(EOF)才原子替换为最终文件名。</summary>
    private static async Task<bool> TranscodeAsync(string sourcePath, string targetPath, int widthCap)
    {
        var tmp = VideoCache.TempPathFor(targetPath);
        var started = DateTime.UtcNow;
        try
        {
            var done = new TaskCompletionSource<MpvEndFileReason>(TaskCreationOptions.RunContinuationsAsynchronously);
            EncodeContext ctx;
            t_job = new EncodeJob(tmp, widthCap);
            try
            {
                ctx = new EncodeContext();
            }
            finally
            {
                t_job = null;
            }
            try
            {
                ctx.EndFile += (_, e) => done.TrySetResult(e.Reason);
                ctx.LoadFile(sourcePath).Invoke();
                var finished = await Task.WhenAny(done.Task, Task.Delay(Timeout)).ConfigureAwait(false);
                if (finished != done.Task || done.Task.Result != MpvEndFileReason.EndOfFile)
                {
                    System.Console.Error.WriteLine(
                        $"MCKURO-VIDEO 转码未完成(继续用原片): {(finished == done.Task ? done.Task.Result.ToString() : "timeout")}");
                    return false;
                }
            }
            finally
            {
                // mpv_terminate_destroy:编码模式下这一步写 mp4 尾部(moov),必须在移动文件之前
                ctx.Dispose();
            }

            if (!VideoCache.IsUsable(tmp))
            {
                return false;
            }
            File.Move(tmp, targetPath, overwrite: true);
            System.Console.Error.WriteLine(
                $"MCKURO-VIDEO 转码完成 w{widthCap} {new FileInfo(sourcePath).Length / 1048576.0:F1}MB→" +
                $"{new FileInfo(targetPath).Length / 1048576.0:F1}MB 用时 {(DateTime.UtcNow - started).TotalSeconds:F1}s");
            return true;
        }
        finally
        {
            VideoCache.TryDeleteFile(tmp);
        }
    }

    private sealed record EncodeJob(string OutputPath, int WidthCap);

    /// <summary>
    /// 构造期传给 <see cref="EncodeContext.OnPreInitialize"/> 的任务参数。
    /// 与 <c>VideoBackgroundControl.t_pendingQuality</c> 同一原因:基类构造器先于派生字段赋值回调
    /// OnPreInitialize,而编码选项(o/ovc)必须在 mpv_initialize 之前设置,只能经线程槽传入。
    /// </summary>
    [ThreadStatic]
    private static EncodeJob? t_job;

    /// <summary>libmpv 编码模式上下文:设置 <c>o</c> 后 mpv 自动套用内置 encoding 预设(vo/ao=lavc)。</summary>
    private sealed class EncodeContext : MpvContext
    {
        protected override void OnPreInitialize()
        {
            var job = t_job ?? throw new InvalidOperationException("EncodeContext 必须经 TranscodeAsync 创建");
            SetOptionString("o", job.OutputPath);
            SetOptionString("of", "mp4");
            // libx264 veryfast:实测 9.1 MB(原片 11.4 MB),比 h264_amf/h264_mf 小一半以上,三平台 libmpv 都带。
            // 线程数与播放侧解码线程同为 4,不在后台占满 CPU。
            SetOptionString("ovc", "libx264");
            SetOptionString("ovcopts", "preset=veryfast,crf=20,threads=4");
            SetOptionString("vf", $"scale=w={job.WidthCap}:h=-2");
            SetOptionString("aid", "no");
            SetOptionString("sid", "no");
            SetOptionString("hwdec", "auto-copy-safe");
            SetOptionString("vd-lavc-threads", "4");
            SetOptionString("load-scripts", "no");
        }
    }
}
