namespace Sparkle.Live2DView;

/// <summary>
/// 「自带动画」的分层播放计划。
///
/// <para><b>背景</b>:第三方「分层 PSD → Cubism」导出器会把互不相干的肢体动作
/// (左臂/右臂/左腿/右腿各一条)全部塞进同一个 <c>Idle</c> 动作组。而 Cubism 的语义里
/// <c>Idle</c> 组是<b>备选</b> —— 官方 <c>LAppModel.Update()</c> 用
/// <c>StartRandomMotion("Idle", PriorityIdle)</c> 每次随机播<b>一条</b>,
/// 于是四个肢体永远只有一个在动。实机表现就是「模型基本是静止的」。</para>
///
/// <para>这类导出器自带的网页播放器并非如此:它把模型里所有 <c>autoplay</c> 轨道
/// <b>同时</b>驱动(见 puppetloom-web.js 里 <c>for (const r of behaviors) r.autoplay &amp;&amp; push(...)</c>),
/// 每条轨道只负责自己的参数,天然互不干扰。</para>
///
/// <para>本类把这一语义补回来:按「驱动的参数互不重叠」把动作分层,层内动作可并行驱动。
/// 对官方模型(Hiyori 等)而言,同一 Idle 组里所有动作驱动的是同一批参数(互为备选),
/// 因此分层结果仍是 1 条 —— 行为与官方一致,不会引入回归。</para>
/// </summary>
public static class Live2DMotionPlan
{
    /// <summary>
    /// 贪婪选出可同时播放的动作下标:按给定顺序取,跳过与已选动作驱动同一参数的动作。
    /// 参数重叠的动作视为「备选」,只保留先出现的那条;不驱动任何参数的动作直接跳过
    /// (它不会产生任何可见效果)。
    /// </summary>
    public static IReadOnlyList<int> SelectConcurrentLayers(
        IReadOnlyList<IReadOnlyCollection<string>> drivenParameters)
    {
        ArgumentNullException.ThrowIfNull(drivenParameters);

        var selected = new List<int>();
        var claimed = new HashSet<string>(StringComparer.Ordinal);

        for (int i = 0; i < drivenParameters.Count; i++)
        {
            IReadOnlyCollection<string>? targets = drivenParameters[i];
            if (targets is null || targets.Count == 0)
            {
                continue;
            }

            bool conflict = false;
            foreach (string id in targets)
            {
                if (claimed.Contains(id))
                {
                    conflict = true;
                    break;
                }
            }

            if (conflict)
            {
                continue;
            }

            foreach (string id in targets)
            {
                claimed.Add(id);
            }

            selected.Add(i);
        }

        return selected;
    }
}
