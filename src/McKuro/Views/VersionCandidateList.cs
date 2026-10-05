using System;
using System.Collections.Generic;
using System.Linq;

namespace McKuro.Views;

/// <summary>
/// 「指定本地游戏版本」下拉的候选列表装配(纯逻辑,无 Avalonia 依赖,便于单测)。
/// <para>
/// 背景:该下拉<b>不可编辑</b>(可编辑会让光标闪在框里、还能手输任意文本)。
/// 但"当前本地记录"未必出现在服务端候选里(例如游戏被外部渠道更新到比清单更新的版本,
/// 或处于补丁表未收录的版本)—— 不可编辑后就没法再靠手输兜底,
/// 因此这里把该值<b>补进候选</b>并选中,保证它始终可选。
/// </para>
/// </summary>
public static class VersionCandidateList
{
    /// <summary>
    /// 计算下拉的最终候选与选中下标。
    /// </summary>
    /// <param name="candidates">服务端/缓存给出的候选版本(可能为空)。</param>
    /// <param name="selected">
    /// 期望选中的版本(通常是当前本地记录);为 null/空白时选第一个候选。
    /// 不在 <paramref name="candidates"/> 中时会被插入列表首位。
    /// </param>
    /// <returns>候选列表(不可为空)与选中下标(列表为空时为 -1)。</returns>
    /// <remarks>
    /// 顺序约定:候选原样保留(服务端已按新→旧排序,不能打乱),
    /// <b>仅当</b>目标版本不在候选里时才插到最前 —— 它属于"清单未收录的当前值",
    /// 插首位既保证可选,也不会在正常路径下改变列表观感。
    /// </remarks>
    public static (IReadOnlyList<string> Items, int SelectedIndex) Build(
        IReadOnlyList<string>? candidates, string? selected)
    {
        var items = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void Add(string? value)
        {
            var trimmed = value?.Trim() ?? "";
            if (trimmed.Length > 0 && seen.Add(trimmed))
            {
                items.Add(trimmed);
            }
        }

        foreach (var candidate in candidates ?? [])
        {
            Add(candidate);
        }

        var want = selected?.Trim() ?? "";
        if (want.Length > 0 && !seen.Contains(want))
        {
            // 当前值不在清单里(游戏被外部渠道更新到更新的版本等):补进候选,否则不可编辑后无法选中
            items.Insert(0, want);
            seen.Add(want);
        }

        if (items.Count == 0)
        {
            return ([], -1);
        }

        var index = want.Length == 0
            ? 0
            : items.FindIndex(x => x.Equals(want, StringComparison.OrdinalIgnoreCase));
        return (items, index < 0 ? 0 : index);
    }
}
