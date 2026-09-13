namespace Sparkle.Live2DView;

public sealed class Live2DModelLoadFailedEventArgs : EventArgs
{
    public Live2DModelLoadFailedEventArgs(
        string modelDirectory,
        string modelName,
        Exception exception)
    {
        ModelDirectory = modelDirectory;
        ModelName = modelName;
        Exception = exception;
    }

    public string ModelDirectory { get; }
    public string ModelName { get; }
    public Exception Exception { get; }
}
