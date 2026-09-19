using System.Diagnostics;

namespace DDT.Agent;

public sealed class ProcessAgentRelauncher : IAgentRelauncher
{
    public async Task<int> RunAsync(string path, IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        // Without shell execution and redirection the new agent shares this console, so it can ask for a
        // sign in. startnet.cmd waits on this process, which in turn waits on the new one.
        ProcessStartInfo start = new(path) { UseShellExecute = false };

        foreach (string argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        using Process process = Process.Start(start) ?? throw new InvalidOperationException($"{path} did not start.");

        // Not cancellable: Ctrl+C reaches the new agent as well, which stops on its own.
        await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);

        return process.ExitCode;
    }
}
