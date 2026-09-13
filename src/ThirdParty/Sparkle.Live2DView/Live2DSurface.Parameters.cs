using Sparkle.Live2DView.Internal;

namespace Sparkle.Live2DView;

public sealed partial class Live2DSurface
{
    private readonly ParameterController _parameters = new();

    public IReadOnlyList<string> Parameters => _parameters.Ids;

    public IReadOnlyList<Live2DParameterInfo> GetParameters()
    {
        return _parameters.GetParameters();
    }

    public bool TryGetParameter(string id, out Live2DParameterInfo? parameter)
    {
        return _parameters.TryGetParameter(id, out parameter);
    }

    public bool SetParameterValue(string id, float value, float weight = 1)
    {
        return QueueParameter(id, value, weight, Live2DParameterBlendMode.Set);
    }

    public bool AddParameterValue(string id, float value, float weight = 1)
    {
        return QueueParameter(id, value, weight, Live2DParameterBlendMode.Add);
    }

    public bool MultiplyParameterValue(string id, float value, float weight = 1)
    {
        return QueueParameter(id, value, weight, Live2DParameterBlendMode.Multiply);
    }

    public int SetParameterValues(IEnumerable<KeyValuePair<string, float>> values, float weight = 1)
    {
        int count = _parameters.Queue(values, weight);
        RequestFrameWhen(count > 0);
        return count;
    }

    public bool SetParameterOverride(
        string id,
        float value,
        Live2DParameterBlendMode blendMode = Live2DParameterBlendMode.Set,
        float weight = 1)
    {
        bool changed = _parameters.SetOverride(id, value, weight, blendMode);
        RequestFrameWhen(changed);
        return changed;
    }

    public int SetParameterOverrides(
        IEnumerable<KeyValuePair<string, float>> values,
        Live2DParameterBlendMode blendMode = Live2DParameterBlendMode.Set,
        float weight = 1)
    {
        int count = 0;
        foreach ((string id, float value) in values)
        {
            if (_parameters.SetOverride(id, value, weight, blendMode))
                count++;
        }

        RequestFrameWhen(count > 0);
        return count;
    }

    public bool TryGetParameterOverride(string id, out Live2DParameterOverride parameterOverride)
    {
        return _parameters.TryGetOverride(id, out parameterOverride);
    }

    public bool RemoveParameterOverride(string id)
    {
        bool removed = _parameters.RemoveOverride(id);
        RequestFrameWhen(removed);
        return removed;
    }

    public void ClearParameterOverrides()
    {
        _parameters.ClearOverrides();
        RequestNextFrameRendering();
    }

    public bool ResetParameter(string id)
    {
        bool reset = _parameters.Reset(id);
        RequestFrameWhen(reset);
        return reset;
    }

    public void ResetAllParameters()
    {
        _parameters.ResetAll();
        RequestNextFrameRendering();
    }

    private bool QueueParameter(
        string id,
        float value,
        float weight,
        Live2DParameterBlendMode blendMode)
    {
        bool queued = _parameters.Queue(id, value, weight, blendMode);
        RequestFrameWhen(queued);
        return queued;
    }

    private void RequestFrameWhen(bool condition)
    {
        if (condition)
            RequestNextFrameRendering();
    }
}
