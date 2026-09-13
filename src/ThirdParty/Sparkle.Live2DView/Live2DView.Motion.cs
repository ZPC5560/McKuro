namespace Sparkle.Live2DView;

public sealed partial class Live2DView
{
    public IReadOnlyList<string> Motions => _surface.Motions;
    public IReadOnlyList<string> Expressions => _surface.Expressions;
    public string? CurrentExpression => _surface.CurrentExpression;

    public event EventHandler<string>? MotionStarted
    {
        add => _surface.MotionStarted += value;
        remove => _surface.MotionStarted -= value;
    }

    public event EventHandler<string>? MotionFinished
    {
        add => _surface.MotionFinished += value;
        remove => _surface.MotionFinished -= value;
    }

    public bool PlayMotion(string name)
    {
        return _surface.PlayMotion(name);
    }

    public bool PlayMotion(string name, Live2DMotionPriority priority)
    {
        return _surface.PlayMotion(name, priority);
    }

    public string PlayNextMotion()
    {
        return _surface.PlayNextMotion();
    }

    public string PlayNextMotion(Live2DMotionPriority priority)
    {
        return _surface.PlayNextMotion(priority);
    }

    public void PlayRandomMotion(string group = "Idle")
    {
        _surface.PlayRandomMotion(group);
    }

    public bool PlayRandomMotion(string group, Live2DMotionPriority priority)
    {
        return _surface.PlayRandomMotion(group, priority);
    }

    public bool StopMotion()
    {
        return _surface.StopMotion();
    }

    public void SetAutomaticIdle(bool enabled)
    {
        _surface.SetAutomaticIdle(enabled);
    }

    public bool SetExpression(string name)
    {
        return _surface.SetExpression(name);
    }

    public bool SetRandomExpression()
    {
        return _surface.SetRandomExpression();
    }

    public void ClearExpression()
    {
        _surface.ClearExpression();
    }
}
