using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;

namespace DDT.Agent.Deployment;

// Runs a Windows tool and puts everything it prints into the machine log, because on a real PC that log is the
// only record of why diskpart, bcdboot or reagentc refused.
public sealed class ToolRunner(AgentLog log, TimeProvider timeProvider)
{
    public async Task RunAsync(string fileName, IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(fileName);
        ArgumentNullException.ThrowIfNull(arguments);

        string tool = Path.GetFileName(fileName);
        log.Information($"Running {CommandLine(fileName, arguments)}");

        ProcessStartInfo start = new(fileName)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        foreach (string argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        long started = timeProvider.GetTimestamp();
        using Process process = Start(start, tool);

        // No tool DDT runs reads its input; closing it keeps one that asks a question from waiting for ever.
        process.StandardInput.Close();

        Task output = ForwardAsync(process.StandardOutput, log.Information);
        Task errors = ForwardAsync(process.StandardError, log.Warning);

        try
        {
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);

            throw;
        }

        await Task.WhenAll(output, errors).ConfigureAwait(false);

        TimeSpan elapsed = timeProvider.GetElapsedTime(started);
        int exitCode = process.ExitCode;
        log.Information(string.Create(
            CultureInfo.InvariantCulture,
            $"{tool} ended with exit code 0x{exitCode:X8} after {elapsed.TotalSeconds:0.0} s."));

        if (exitCode != 0)
        {
            throw new DeploymentStepException(string.Create(
                CultureInfo.InvariantCulture,
                $"{tool} failed with exit code 0x{exitCode:X8}. Its output is in the machine log."));
        }
    }

    public static string CommandLine(string fileName, IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        return string.Join(' ', new[] { fileName }.Concat(arguments).Select(Quote));
    }

    private static Process Start(ProcessStartInfo start, string tool)
    {
        try
        {
            return Process.Start(start) ?? throw new DeploymentStepException($"{tool} did not start.");
        }
        catch (Win32Exception exception)
        {
            throw new DeploymentStepException($"{start.FileName} cannot be started: {exception.Message}", exception);
        }
    }

    private static string Quote(string argument) =>
        argument.Length == 0 || argument.Contains(' ', StringComparison.Ordinal) ? $"\"{argument}\"" : argument;

    private static async Task ForwardAsync(StreamReader reader, Action<string> write)
    {
        while (await reader.ReadLineAsync().ConfigureAwait(false) is { } line)
        {
            if (!string.IsNullOrWhiteSpace(line))
            {
                write(line.TrimEnd());
            }
        }
    }
}
