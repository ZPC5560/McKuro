namespace Sparkle.Live2DView;

public sealed partial class Live2DView
{
    public IReadOnlyList<string> Parameters => _surface.Parameters;
    public IReadOnlyList<Live2DParameterInfo> ParameterDetails => _surface.GetParameters();

    public IReadOnlyList<Live2DParameterInfo> GetParameters()
    {
        return _surface.GetParameters();
    }

    public bool TryGetParameter(string id, out Live2DParameterInfo? parameter)
    {
        return _surface.TryGetParameter(id, out parameter);
    }

    public float GetParameterValue(string id)
    {
        if (TryGetParameter(id, out Live2DParameterInfo? parameter) && parameter is not null)
            return parameter.Value;

        throw new KeyNotFoundException($"模型中不存在参数 {id}，或者模型尚未加载。");
    }

    public bool SetParameterValue(string id, float value, float weight = 1)
    {
        return _surface.SetParameterValue(id, value, weight);
    }

    public bool AddParameterValue(string id, float value, float weight = 1)
    {
        return _surface.AddParameterValue(id, value, weight);
    }

    public bool MultiplyParameterValue(string id, float value, float weight = 1)
    {
        return _surface.MultiplyParameterValue(id, value, weight);
    }

    public int SetParameterValues(IEnumerable<KeyValuePair<string, float>> values, float weight = 1)
    {
        return _surface.SetParameterValues(values, weight);
    }

    public bool SetParameterOverride(
        string id,
        float value,
        Live2DParameterBlendMode blendMode = Live2DParameterBlendMode.Set,
        float weight = 1)
    {
        return _surface.SetParameterOverride(id, value, blendMode, weight);
    }

    public int SetParameterOverrides(
        IEnumerable<KeyValuePair<string, float>> values,
        Live2DParameterBlendMode blendMode = Live2DParameterBlendMode.Set,
        float weight = 1)
    {
        return _surface.SetParameterOverrides(values, blendMode, weight);
    }

    public bool TryGetParameterOverride(string id, out Live2DParameterOverride parameterOverride)
    {
        return _surface.TryGetParameterOverride(id, out parameterOverride);
    }

    public bool RemoveParameterOverride(string id)
    {
        return _surface.RemoveParameterOverride(id);
    }

    public void ClearParameterOverrides()
    {
        _surface.ClearParameterOverrides();
    }

    public bool ResetParameter(string id)
    {
        return _surface.ResetParameter(id);
    }

    public void ResetAllParameters()
    {
        _surface.ResetAllParameters();
    }
}
