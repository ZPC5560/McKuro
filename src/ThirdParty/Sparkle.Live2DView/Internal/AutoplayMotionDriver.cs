using System.Text.Json;
using Live2DCSharpSDK.App;
using Live2DCSharpSDK.Framework.Model;
using Live2DCSharpSDK.Framework.Motion;

namespace Sparkle.Live2DView.Internal;

/// <summary>
/// 「自带动画」并行播放驱动。
///
/// <para><b>为什么需要它</b>:第三方「分层 PSD → Cubism」导出器(如 PuppetLoom)把互不相干的
/// 肢体动作各写成一条 <b>autoplay</b> 动作(左臂/右臂/左腿/右腿),并在其网页播放器里
/// <b>同时</b>驱动所有 autoplay 轨道。但转成 Cubism 后,这些动作被塞进 <c>Idle</c> 组,
/// 而 Cubism 的 <c>Idle</c> 语义是 <b>备选</b>:官方 <c>LAppModel.Update()</c> 每次
/// <c>StartRandomMotion("Idle", …)</c> 只随机播<b>一条</b>。实机表现:四个肢体里永远只有一个在动,
/// 看起来"模型是静止的"。</para>
///
/// <para><b>做法</b>:为每条 autoplay 动作建一个独立的 <see cref="CubismMotionManager"/>。
/// 必须独立 —— 同一个 manager 上 <c>StartMotion</c> 会把<b>已有动作全部标记 fade-out</b>
/// (官方语义:一次只有一个主动作),后起的会把先起的顶掉。</para>
///
/// <para><b>只作用于显式标记了 autoplay 的模型</b>:候选仅为组名以 "autoplay" 开头的动作。
/// 官方模型(Hiyori 等)没有该组,因此行为完全不变(仍走 SDK 原本的随机 Idle),
/// 不会引入回归。</para>
///
/// <para><b>顺带修正一个 SDK 移植缺口</b>:C# 版 <c>CubismMotion</c> 只把 <c>Meta.Loop</c>
/// 存进内部 <c>_motionData.Loop</c>,<b>从未</b>赋给公开的 <c>IsLoop</c>,于是
/// <c>motion3.json</c> 里 <c>"Loop": true</c> 被忽略,动作播一遍就结束(实机表现为肢体逐个停住)。
/// 这里按文件里的真实 <c>Meta.Loop</c> 显式回填。</para>
/// </summary>
internal sealed class AutoplayMotionDriver
{
    /// <summary>导出器给"自动播放"动作使用的组名前缀(PuppetLoom:puppetloom.behaviors[].autoplay)。</summary>
    private const string AutoplayGroupPrefix = "autoplay";

    private readonly List<CubismMotionManager> _managers = [];
    private LAppModel? _model;
    private Action<LAppModel>? _frameUpdate;

    /// <summary>当前实际并行驱动的动作层数(0 = 该模型没有 autoplay 动作,应交回 SDK 默认随机 Idle)。</summary>
    public int LayerCount => _managers.Count;

    /// <summary>
    /// 按模型的 autoplay 组建立并行层并起播。总是先 <see cref="Detach"/>,
    /// 因此重复调用安全(切形态/重载模型时不会残留旧订阅)。
    /// 自身不抛异常:任何失败都退化为"没有并行层",交回 SDK 默认的随机 Idle 行为,
    /// 绝不因此让模型加载失败。
    /// </summary>
    public void Attach(LAppModel model)
    {
        if (model is null)
        {
            return;
        }

        Detach();

        try
        {
            foreach (AutoplayLayer layer in SelectLayers(model))
            {
                if (layer.Motion is CubismMotion cubism)
                {
                    cubism.IsLoop = layer.Loop;
                }

                var manager = new CubismMotionManager();
                manager.StartMotionPriority(layer.Motion, MotionPriority.PriorityForce);
                _managers.Add(manager);
            }
        }
        catch (Exception exception)
        {
            System.Diagnostics.Debug.WriteLine(
                $"[Sparkle.Live2DView] 建立 autoplay 并行层失败，回退为默认 Idle：{exception.Message}");
            _managers.Clear();
            return;
        }

        if (_managers.Count == 0)
        {
            return;
        }

        _model = model;
        _frameUpdate = current =>
        {
            // 与 LAppModel.Update() 用同一个时钟(由 LAppDelegate.Run(tick) 写入)。
            float deltaTime = LAppPal.DeltaTime;
            if (deltaTime <= 0f || !float.IsFinite(deltaTime))
            {
                return;
            }

            foreach (CubismMotionManager manager in _managers)
            {
                manager.UpdateMotion(current.Model, deltaTime);
            }
        };

        // 注意:CustomValueUpdate 由 ParameterController 统一置真并在其 Detach 时恢复,
        // 这里只订阅回调,不重复改写该标志,避免两个持有者互相覆盖恢复值。
        model.ValueUpdate += _frameUpdate;
    }

    /// <summary>停止并行层并解除订阅。未附着时为空操作。</summary>
    public void Detach()
    {
        if (_model is not null && _frameUpdate is not null)
        {
            _model.ValueUpdate -= _frameUpdate;
        }

        _model = null;
        _frameUpdate = null;
        _managers.Clear();
    }

    private static List<AutoplayLayer> SelectLayers(LAppModel model)
    {
        var result = new List<AutoplayLayer>();

        Dictionary<string, List<Live2DCSharpSDK.Framework.ModelSettingObj.FileReference.Motion>>? groups =
            model._modelSetting.FileReferences?.Motions;
        if (groups is null || groups.Count == 0)
        {
            return result;
        }

        // 组名排序保证选择结果稳定(字典序决定重叠时的取舍)。
        var candidates = new List<(string Group, int Index)>();
        foreach (string group in groups.Keys.OrderBy(name => name, StringComparer.Ordinal))
        {
            if (!group.StartsWith(AutoplayGroupPrefix, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            for (int index = 0; index < groups[group].Count; index++)
            {
                candidates.Add((group, index));
            }
        }

        if (candidates.Count == 0)
        {
            return result;
        }

        var motionInfos = new List<MotionInfo>(candidates.Count);
        foreach ((string group, int index) in candidates)
        {
            motionInfos.Add(ReadMotionInfo(model, group, index));
        }

        IReadOnlyList<int> keep = Live2DMotionPlan.SelectConcurrentLayers(
            motionInfos.Select(info => info.DrivenParameters).ToArray());

        foreach (int i in keep)
        {
            (string group, int index) = candidates[i];
            if (!model._motions.TryGetValue($"{group}_{index}", out ACubismMotion? motion) || motion is null)
            {
                continue;
            }

            result.Add(new AutoplayLayer(motion, motionInfos[i].Loop));
        }

        return result;
    }

    /// <summary>读取单条动作驱动的参数集合与循环标记(取自 motion3.json 本身)。</summary>
    private static MotionInfo ReadMotionInfo(LAppModel model, string group, int index)
    {
        try
        {
            string relative = model._modelSetting.FileReferences.Motions[group][index].File;
            string path = Path.GetFullPath(model._modelHomeDir + relative);
            if (!File.Exists(path))
            {
                return MotionInfo.Empty;
            }

            using FileStream stream = File.OpenRead(path);
            using JsonDocument document = JsonDocument.Parse(stream);
            JsonElement root = document.RootElement;

            bool loop = true;
            if (root.TryGetProperty("Meta", out JsonElement meta)
                && meta.TryGetProperty("Loop", out JsonElement loopElement)
                && loopElement.ValueKind is JsonValueKind.True or JsonValueKind.False)
            {
                loop = loopElement.GetBoolean();
            }

            var ids = new HashSet<string>(StringComparer.Ordinal);
            if (root.TryGetProperty("Curves", out JsonElement curves) && curves.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement curve in curves.EnumerateArray())
                {
                    if (curve.TryGetProperty("Target", out JsonElement target)
                        && string.Equals(target.GetString(), "Parameter", StringComparison.Ordinal)
                        && curve.TryGetProperty("Id", out JsonElement id)
                        && id.GetString() is { Length: > 0 } value)
                    {
                        ids.Add(value);
                    }
                }
            }

            return new MotionInfo(ids, loop);
        }
        catch (Exception exception)
        {
            System.Diagnostics.Debug.WriteLine(
                $"[Sparkle.Live2DView] 读取动作轨道失败({group}_{index})：{exception.Message}");
            return MotionInfo.Empty;
        }
    }

    private readonly record struct AutoplayLayer(ACubismMotion Motion, bool Loop);

    private readonly record struct MotionInfo(IReadOnlyCollection<string> DrivenParameters, bool Loop)
    {
        public static MotionInfo Empty { get; } = new([], true);
    }
}
