using System.Collections.Concurrent;
using Live2DCSharpSDK.App;
using Live2DCSharpSDK.Framework.Model;

namespace Sparkle.Live2DView.Internal;

internal sealed class ParameterController
{
    private readonly object _gate = new();
    private readonly ConcurrentQueue<ParameterCommand> _pending = new();
    private readonly Dictionary<string, ParameterEntry> _entries = new(StringComparer.Ordinal);
    private readonly Dictionary<int, Live2DParameterOverride> _overrides = [];
    private LAppModel? _model;
    private Action<LAppModel>? _frameUpdate;
    private bool _originalCustomValueUpdate;

    public IReadOnlyList<string> Ids
    {
        get
        {
            lock (_gate) return _entries.Keys.ToArray();
        }
    }

    public void Attach(LAppModel model, Action<LAppModel> pointerUpdate)
    {
        Detach();

        CubismModel coreModel = model.Model;
        lock (_gate)
        {
            _model = model;
            int count = Math.Min(coreModel.GetParameterCount(), coreModel.ParameterIds.Count);
            for (int index = 0; index < count; index++)
            {
                string id = coreModel.ParameterIds[index];
                _entries[id] = new ParameterEntry(
                    index,
                    id,
                    coreModel.GetParameterValue(index),
                    coreModel.GetParameterDefaultValue(index),
                    coreModel.GetParameterMinimumValue(index),
                    coreModel.GetParameterMaximumValue(index));
            }
        }

        _originalCustomValueUpdate = model.CustomValueUpdate;
        _frameUpdate = currentModel =>
        {
            pointerUpdate(currentModel);
            ApplyCommands(currentModel.Model);
        };
        model.CustomValueUpdate = true;
        model.ValueUpdate += _frameUpdate;
    }

    public void Detach()
    {
        if (_model is not null && _frameUpdate is not null)
        {
            _model.ValueUpdate -= _frameUpdate;
            _model.CustomValueUpdate = _originalCustomValueUpdate;
        }

        _model = null;
        _frameUpdate = null;
        _pending.Clear();

        lock (_gate)
        {
            _entries.Clear();
            _overrides.Clear();
        }
    }

    public IReadOnlyList<Live2DParameterInfo> GetParameters()
    {
        lock (_gate)
        {
            return _entries.Values
                .Select(item => item.ToInfo())
                .ToArray();
        }
    }

    public bool TryGetParameter(string id, out Live2DParameterInfo? parameter)
    {
        lock (_gate)
        {
            if (_entries.TryGetValue(id, out ParameterEntry? entry))
            {
                parameter = entry.ToInfo();
                return true;
            }
        }

        parameter = null;
        return false;
    }

    public bool Queue(string id, float value, float weight, Live2DParameterBlendMode blendMode)
    {
        if (!float.IsFinite(value) || !float.IsFinite(weight)) return false;

        lock (_gate)
        {
            if (!_entries.TryGetValue(id, out ParameterEntry? entry)) return false;
            _pending.Enqueue(new ParameterCommand(entry.Index, value, Math.Clamp(weight, 0, 1), blendMode));
            return true;
        }
    }

    public int Queue(IEnumerable<KeyValuePair<string, float>> values, float weight)
    {
        int accepted = 0;
        foreach ((string id, float value) in values)
        {
            if (Queue(id, value, weight, Live2DParameterBlendMode.Set)) accepted++;
        }
        return accepted;
    }

    public bool SetOverride(string id, float value, float weight, Live2DParameterBlendMode blendMode)
    {
        if (!float.IsFinite(value) || !float.IsFinite(weight)) return false;

        lock (_gate)
        {
            if (!_entries.TryGetValue(id, out ParameterEntry? entry)) return false;
            _overrides[entry.Index] = new Live2DParameterOverride(value, Math.Clamp(weight, 0, 1), blendMode);
            return true;
        }
    }

    public bool TryGetOverride(string id, out Live2DParameterOverride parameterOverride)
    {
        lock (_gate)
        {
            if (_entries.TryGetValue(id, out ParameterEntry? entry) &&
                _overrides.TryGetValue(entry.Index, out parameterOverride))
                return true;
        }

        parameterOverride = default;
        return false;
    }

    public bool RemoveOverride(string id)
    {
        lock (_gate)
        {
            return _entries.TryGetValue(id, out ParameterEntry? entry) && _overrides.Remove(entry.Index);
        }
    }

    public void ClearOverrides()
    {
        lock (_gate) _overrides.Clear();
    }

    public bool Reset(string id)
    {
        lock (_gate)
        {
            if (!_entries.TryGetValue(id, out ParameterEntry? entry)) return false;
            _overrides.Remove(entry.Index);
            _pending.Enqueue(new ParameterCommand(entry.Index, entry.DefaultValue, 1, Live2DParameterBlendMode.Set));
            return true;
        }
    }

    public void ResetAll()
    {
        lock (_gate)
        {
            _overrides.Clear();
            foreach (ParameterEntry entry in _entries.Values)
                _pending.Enqueue(new ParameterCommand(entry.Index, entry.DefaultValue, 1, Live2DParameterBlendMode.Set));
        }
    }

    public void CaptureValues()
    {
        LAppModel? model = _model;
        if (model is null) return;

        lock (_gate)
        {
            foreach (ParameterEntry entry in _entries.Values)
                entry.Value = model.Model.GetParameterValue(entry.Index);
        }
    }

    private void ApplyCommands(CubismModel model)
    {
        while (_pending.TryDequeue(out ParameterCommand command))
            Apply(model, command.Index, command.Value, command.Weight, command.BlendMode);

        lock (_gate)
        {
            foreach ((int index, Live2DParameterOverride item) in _overrides)
                Apply(model, index, item.Value, item.Weight, item.BlendMode);
        }
    }

    private static void Apply(
        CubismModel model,
        int index,
        float value,
        float weight,
        Live2DParameterBlendMode blendMode)
    {
        switch (blendMode)
        {
            case Live2DParameterBlendMode.Set:
                model.SetParameterValue(index, value, weight);
                break;
            case Live2DParameterBlendMode.Add:
                model.AddParameterValue(index, value, weight);
                break;
            case Live2DParameterBlendMode.Multiply:
                model.MultiplyParameterValue(index, value, weight);
                break;
        }
    }

    private sealed class ParameterEntry(
        int index,
        string id,
        float value,
        float defaultValue,
        float minimumValue,
        float maximumValue)
    {
        public int Index { get; } = index;
        public string Id { get; } = id;
        public float Value { get; set; } = value;
        public float DefaultValue { get; } = defaultValue;
        public float MinimumValue { get; } = minimumValue;
        public float MaximumValue { get; } = maximumValue;

        public Live2DParameterInfo ToInfo() =>
            new(Id, Value, DefaultValue, MinimumValue, MaximumValue);
    }

    private readonly record struct ParameterCommand(
        int Index,
        float Value,
        float Weight,
        Live2DParameterBlendMode BlendMode);
}
