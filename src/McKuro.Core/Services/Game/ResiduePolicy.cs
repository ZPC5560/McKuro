using System;
using System.Collections.Generic;
using System.Linq;

namespace McKuro.Core.Services.Game;

/// <summary>一次「安装完成后体积自检」的结果。</summary>
public sealed class ResidueScanResult
{
    public static ResidueScanResult None { get; } = new() { Report = ResidueReport.Empty };

    /// <summary>可回收的残留清单。</summary>
    public required ResidueReport Report { get; init; }

    /// <summary>游戏目录实测占用(含残留)。</summary>
    public long ActualBytes { get; init; }

    /// <summary>服务端清单里所有文件的大小总和(0 = 没拿到清单,不参与比对)。</summary>
    public long ManifestBytes { get; init; }

    /// <summary>扣掉残留后的"应有体积"。</summary>
    public long EffectiveBytes => Math.Max(0, ActualBytes - Report.ReclaimBytes);

    /// <summary>实测体积比清单总量高出的比例(清单未知时返回 0)。</summary>
    public double ExcessRatio => ManifestBytes > 0
        ? Math.Max(0, (double)(ActualBytes - Report.ReclaimBytes - ManifestBytes) / ManifestBytes)
        : 0;
}

/// <summary>
/// 「要不要弹窗问用户删残留」的纯函数策略(可单测,避免把阈值散落在 UI 里)。
/// <para>
/// 用户需求(2026-10):安装完成后比对游戏目录体积与服务器清单体积,<b>明显过大时</b>
/// 后台校验重复文件并弹窗询问是否删除。
/// </para>
/// <para>
/// 两个独立的触发条件(任一满足即提示):
/// </para>
/// <list type="number">
/// <item><b>残留本身够大</b>：可回收 ≥ <see cref="MinPromptBytes"/>。哪怕目录体积还在清单范围内
/// (清单体积只是下载量,不含运行时存档),一份 100MB 的孤儿备份也值得清。</item>
/// <item><b>目录明显超出清单</b>：扣掉残留后仍比清单大 <see cref="ExcessTolerance"/> 以上。
/// 这种情况说明还有"不在已知残留清单里"的重复文件,提示用户先去用「修复游戏」核对,
/// 我们仍然把已知残留列出来供删。</item>
/// </list>
/// <para>
/// <b>为什么阈值不能设成"只要大于清单就提示"</b>:游戏目录必然包含运行时存档、
/// 日志、以及其它画质资源包(它们本来就不在当前清单里),按"大于清单"判定会天天弹窗。
/// </para>
/// </summary>
public static class ResiduePolicy
{
    /// <summary>可回收体积达到该值就值得提示用户(128 MB)。</summary>
    public const long MinPromptBytes = 128L * 1024 * 1024;

    /// <summary>目录比清单大出这个比例才算"明显过大"(25%)。</summary>
    public const double ExcessTolerance = 0.25;

    /// <summary>是否应该弹窗提示。</summary>
    public static bool ShouldPrompt(ResidueScanResult? result)
    {
        if (result is null || !result.Report.HasResidue)
        {
            return false;
        }
        if (result.Report.ReclaimBytes >= MinPromptBytes)
        {
            return true;
        }
        return result.ManifestBytes > 0 && result.ExcessRatio >= ExcessTolerance;
    }

    /// <summary>
    /// 给用户看的归类摘要(按类别合并,避免列出上千条)。
    /// </summary>
    public static IReadOnlyList<(ResidueKind Kind, int Count, long Bytes)> Summarize(ResidueReport? report)
    {
        if (report is null || !report.HasResidue)
        {
            return [];
        }
        var list = new List<(ResidueKind, int, long)>();
        foreach (var kind in Enum.GetValues<ResidueKind>())
        {
            var items = report.Items.Where(i => i.Kind == kind).ToList();
            if (items.Count == 0)
            {
                continue;
            }
            list.Add((kind, items.Count, items.Sum(i => i.Size)));
        }
        return list;
    }
}
