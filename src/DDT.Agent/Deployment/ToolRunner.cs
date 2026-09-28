// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace DDT.Agent.Deployment;

// Runs a Windows tool and logs all it prints: on a real PC the machine log is the only record of why diskpart, bcdboot
// or reagentc refused. A script run as an account starts through accountProcessStarter, which Process cannot replace.
public sealed class ToolRunner(AgentLog log, TimeProvider timeProvider, IAccountProcessStarter? accountProcessStarter = null) : IToolRunner
{
    // A process that a script starts in the background inherits its output and can keep it open long after the
    // script ended, so the rest of the output is not waited for beyond this.
    public static readonly TimeSpan OutputGrace = TimeSpan.FromSeconds(10);

    public async Task<IReadOnlyList<string>> RunAsync(string fileName, IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(fileName);
        ArgumentNullException.ThrowIfNull(arguments);

        string tool = Path.GetFileName(fileName);
        log.Information($"Running {CommandLine(fileName, arguments)}");

        long started = timeProvider.GetTimestamp();
        using Process process = Start(StartInfo(fileName, arguments), tool);

        List<string> lines = [];
        Task output = ToolOutput.ForwardAsync(process.StandardOutput.BaseStream, line =>
        {
            lines.Add(line);
            log.Information(line);
        });
        Task errors = ToolOutput.ForwardAsync(process.StandardError.BaseStream, log.Warning);

        try
        {
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            Kill(process, tool);

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

        return lines;
    }

    public async Task<int> RunForExitCodeAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        ToolRunOptions options,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(fileName);
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentNullException.ThrowIfNull(options);

        string tool = Path.GetFileName(fileName);
        log.Information($"Running {CommandLine(fileName, arguments)}");

        using CancellationTokenSource timeout = new(options.Timeout ?? Timeout.InfiniteTimeSpan, timeProvider);
        using CancellationTokenSource waiting = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);

        long started = timeProvider.GetTimestamp();
        using IToolProcess process = StartFor(fileName, arguments, options, tool);

        Task output = ToolOutput.ForwardAsync(process.StandardOutput, log.Information);
        Task errors = ToolOutput.ForwardAsync(process.StandardError, log.Warning);

        try
        {
            await process.WaitForExitAsync(waiting.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            process.Kill();

            // The last lines often say why it hung.
            await DrainAsync(output, errors, tool).ConfigureAwait(false);

            if (cancellationToken.IsCancellationRequested)
            {
                throw;
            }

            throw new DeploymentStepException(string.Create(
                CultureInfo.InvariantCulture,
                $"{tool} was still running after {options.Timeout!.Value.TotalMinutes:0.#} minutes, so it was stopped with every process it started."));
        }

        await DrainAsync(output, errors, tool).ConfigureAwait(false);

        TimeSpan elapsed = timeProvider.GetElapsedTime(started);
        int exitCode = process.ExitCode;
        log.Information(string.Create(
            CultureInfo.InvariantCulture,
            $"{tool} ended with exit code {exitCode} after {elapsed.TotalSeconds:0.0} s."));

        return exitCode;
    }

    // As the account when the step runs as one, otherwise as the agent. A run-as step reached this far only past the
    // validator and the step runner, so a missing starter is a wiring mistake, not an operator's.
    private IToolProcess StartFor(string fileName, IReadOnlyList<string> arguments, ToolRunOptions options, string tool)
    {
        if (options.Account is { } account)
        {
            if (accountProcessStarter is null)
            {
                throw new DeploymentStepException($"{tool} was to run as {account.UserName}, but this agent cannot start a process as an account.");
            }

            return accountProcessStarter.Start(account, fileName, arguments, options.WorkingDirectory, options.Environment);
        }

        ProcessStartInfo start = StartInfo(fileName, arguments);

        if (options.WorkingDirectory is { } directory)
        {
            start.WorkingDirectory = directory;
        }

        foreach ((string name, string value) in options.Environment ?? new Dictionary<string, string>())
        {
            start.Environment[name] = value;
        }

        return new ProcessToolProcess(Start(start, tool), log);
    }

    public static string CommandLine(string fileName, IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        return string.Join(' ', new[] { fileName }.Concat(arguments).Select(Quote));
    }

    private static ProcessStartInfo StartInfo(string fileName, IReadOnlyList<string> arguments)
    {
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

        return start;
    }

    private static Process Start(ProcessStartInfo start, string tool)
    {
        Process process;

        try
        {
            process = Process.Start(start) ?? throw new DeploymentStepException($"{tool} did not start.");
        }
        catch (Win32Exception exception)
        {
            throw new DeploymentStepException($"{start.FileName} cannot be started: {exception.Message}", exception);
        }

        // No tool DDT runs reads its input; closing it keeps one that asks a question from waiting for ever.
        process.StandardInput.Close();

        return process;
    }

    private async Task DrainAsync(Task output, Task errors, string tool)
    {
        try
        {
            await Task.WhenAll(output, errors).WaitAsync(OutputGrace, timeProvider).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            log.Warning($"{tool} ended, but a process it started still holds its output. What that process prints is not logged.");
        }
    }

    private void Kill(Process process, string tool)
    {
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch (Exception exception) when (exception is InvalidOperationException or Win32Exception)
        {
            log.Warning($"{tool} could not be stopped ({exception.Message}).");
        }
    }

    private static string Quote(string argument) =>
        argument.Length == 0 || argument.Contains(' ', StringComparison.Ordinal) ? $"\"{argument}\"" : argument;
}
