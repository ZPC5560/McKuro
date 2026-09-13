namespace Sparkle.Live2DView.Internal;

internal enum ModelChangeKind
{
    Load,
    Unload
}

internal sealed class PendingModelChange
{
    private PendingModelChange(
        ModelChangeKind kind,
        string modelDirectory,
        string modelName,
        CancellationToken cancellationToken,
        TaskCompletionSource? completion)
    {
        Kind = kind;
        ModelDirectory = modelDirectory;
        ModelName = modelName;
        CancellationToken = cancellationToken;
        Completion = completion;
    }

    public ModelChangeKind Kind { get; }
    public string ModelDirectory { get; }
    public string ModelName { get; }
    public CancellationToken CancellationToken { get; }
    public TaskCompletionSource? Completion { get; }

    public static PendingModelChange Load(
        string modelDirectory,
        string modelName,
        CancellationToken cancellationToken,
        TaskCompletionSource? completion)
    {
        return new PendingModelChange(
            ModelChangeKind.Load,
            modelDirectory,
            modelName,
            cancellationToken,
            completion);
    }

    public static PendingModelChange Unload(TaskCompletionSource? completion)
    {
        return new PendingModelChange(
            ModelChangeKind.Unload,
            string.Empty,
            string.Empty,
            CancellationToken.None,
            completion);
    }
}
