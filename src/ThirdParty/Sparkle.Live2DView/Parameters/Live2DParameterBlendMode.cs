namespace Sparkle.Live2DView;

public enum Live2DParameterBlendMode
{
    Set,
    Add,
    Multiply
}

public readonly record struct Live2DParameterOverride(
    float Value,
    float Weight,
    Live2DParameterBlendMode BlendMode);
