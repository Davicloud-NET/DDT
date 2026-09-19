using DDT.Core.Wim;

namespace DDT.Core.Tests.Wim;

// Reports on the caller's thread, unlike Progress<T>, so a handler can cancel inside the wimlib callback.
public sealed class RecordingProgress(Action<WimProgress>? onReport = null) : IProgress<WimProgress>
{
    private readonly List<WimProgress> _reports = [];

    public IReadOnlyList<WimProgress> Reports => _reports;

    public void Report(WimProgress value)
    {
        _reports.Add(value);
        onReport?.Invoke(value);
    }
}
