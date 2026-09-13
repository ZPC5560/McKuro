namespace Sparkle.Live2DView;

public sealed class Live2DModelEventArgs : EventArgs
{
    public Live2DModelEventArgs(string modelDirectory, string modelName)
    {
        ModelDirectory = modelDirectory;
        ModelName = modelName;
    }

    public string ModelDirectory { get; }
    public string ModelName { get; }
}
