namespace Sparkle.Live2DView;

public sealed record Live2DParameterInfo(
    string Id,
    float Value,
    float DefaultValue,
    float MinimumValue,
    float MaximumValue);
