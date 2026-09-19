using DDT.Agent.Deployment;

namespace DDT.Agent.Tests;

// Records each tool call as the file name and its arguments joined by spaces, instead of running it. Answer decides
// what a call prints, or throws; without it every call prints nothing.
internal sealed class RecordingToolRunner : IToolRunner
{
    private readonly Lock _lock = new();
    private readonly List<string> _calls = [];

    public Func<string, IReadOnlyList<string>, IReadOnlyList<string>>? Answer { get; set; }

    public List<string> Calls
    {
        get
        {
            lock (_lock)
            {
                return [.. _calls];
            }
        }
    }

    public static string CommandLine(string fileName, params string[] arguments) => string.Join(' ', [fileName, .. arguments]);

    public Task<IReadOnlyList<string>> RunAsync(string fileName, IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            _calls.Add(CommandLine(fileName, [.. arguments]));
        }

        try
        {
            return Task.FromResult(Answer?.Invoke(fileName, arguments) ?? []);
        }
        catch (Exception exception)
        {
            return Task.FromException<IReadOnlyList<string>>(exception);
        }
    }
}
