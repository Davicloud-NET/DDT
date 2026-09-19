namespace DDT.Agent.Tests;

// Records what would have been started and answers with a scripted exit code, or fails to start.
internal sealed class ScriptedRelauncher(Func<int> exitCode) : IAgentRelauncher
{
    public List<(string Path, IReadOnlyList<string> Arguments)> Started { get; } = [];

    public Task<int> RunAsync(string path, IReadOnlyList<string> arguments)
    {
        Started.Add((path, arguments));

        return Task.FromResult(exitCode());
    }
}
