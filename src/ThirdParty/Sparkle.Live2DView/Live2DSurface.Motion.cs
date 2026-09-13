using Live2DCSharpSDK.App;
using Live2DCSharpSDK.Framework.Model;
using Live2DCSharpSDK.Framework.Motion;

namespace Sparkle.Live2DView;

public sealed partial class Live2DSurface
{
    private int _nextMotion;
    private string? _currentExpression;

    public IReadOnlyList<string> Motions => _model?.Motions ?? [];
    public IReadOnlyList<string> Expressions => _model?.Expressions ?? [];
    public string? CurrentExpression => _currentExpression;

    public event EventHandler<string>? MotionStarted;
    public event EventHandler<string>? MotionFinished;

    public string PlayNextMotion()
    {
        return PlayNextMotion(Live2DMotionPriority.Force);
    }

    public string PlayNextMotion(Live2DMotionPriority priority)
    {
        if (_model is null || Motions.Count == 0)
            return "模型尚未加载或没有动作";

        string name = Motions[_nextMotion % Motions.Count];
        _nextMotion++;

        return PlayMotion(name, priority)
            ? $"正在播放 {name}"
            : $"{name} 未播放：优先级不足";
    }

    public bool PlayMotion(string name)
    {
        return PlayMotion(name, Live2DMotionPriority.Force);
    }

    public bool PlayMotion(string name, Live2DMotionPriority priority)
    {
        if (_model is null || !Motions.Contains(name))
            return false;

        CubismMotionQueueEntry? entry = _model.StartMotion(
            name,
            ConvertPriority(priority),
            MotionEnded);

        if (entry is null || !entry.Available)
            return false;

        MotionStarted?.Invoke(this, name);
        return true;
    }

    public void PlayRandomMotion(string group = "Idle")
    {
        PlayRandomMotion(group, Live2DMotionPriority.Force);
    }

    public bool PlayRandomMotion(string group, Live2DMotionPriority priority)
    {
        string prefix = group + "_";
        if (_model is null || !Motions.Any(name => name.StartsWith(prefix, StringComparison.Ordinal)))
            return false;

        object? result = _model.StartRandomMotion(group, ConvertPriority(priority), MotionEnded);
        if (result is not CubismMotionQueueEntry entry || !entry.Available)
            return false;

        MotionStarted?.Invoke(this, $"{group}（随机）");
        return true;
    }

    public bool StopMotion()
    {
        if (_model is null)
            return false;

        bool wasPlaying = !_model._motionManager.IsFinished();
        _model._motionManager.StopAllMotions();
        return wasPlaying;
    }

    public bool SetExpression(string name)
    {
        if (_model is null || !Expressions.Contains(name))
            return false;

        _model.SetExpression(name);
        _currentExpression = name;
        return true;
    }

    public bool SetRandomExpression()
    {
        if (Expressions.Count == 0)
            return false;

        int index = Random.Shared.Next(Expressions.Count);
        return SetExpression(Expressions[index]);
    }

    public void ClearExpression()
    {
        _model?._expressionManager.StopAllMotions();
        _currentExpression = null;
    }

    private void MotionEnded(CubismModel model, ACubismMotion motion)
    {
        if (_model is null || !ReferenceEquals(_model.Model, model))
            return;

        foreach ((string name, ACubismMotion item) in _model._motions)
        {
            if (!ReferenceEquals(item, motion))
                continue;

            MotionFinished?.Invoke(this, name);
            break;
        }
    }

    private static MotionPriority ConvertPriority(Live2DMotionPriority priority)
    {
        return priority switch
        {
            Live2DMotionPriority.None => MotionPriority.PriorityNone,
            Live2DMotionPriority.Idle => MotionPriority.PriorityIdle,
            Live2DMotionPriority.Normal => MotionPriority.PriorityNormal,
            Live2DMotionPriority.Force => MotionPriority.PriorityForce,
            _ => throw new ArgumentOutOfRangeException(nameof(priority))
        };
    }
}
