// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Globalization;
using System.Net;
using System.Text.Json;
using DDT.Agent.Consoles;
using DDT.Contracts.Agents;

namespace DDT.Agent;

// Switches to the agent and the graphical console the server offers, so a DDT update never needs a new boot image.
// It only runs before the first registration. The new agent doesn't get this agent's resume token, so switching later
// would cost an approval.
public sealed class AgentUpdate(
    IAgentServer server,
    IAgentRelauncher relauncher,
    AgentLog log,
    TimeProvider timeProvider,
    RunningAgent agent,
    ConsoleStatus? status = null)
{
    // Six retries wait 90 seconds in all, longer than the server's one minute window.
    private const int MaxRefusals = 6;

    // How long the server is tried for the current agent. Null to try until it answers.
    public TimeSpan? GiveUpAfter { get; init; }

    // Returns the exit code to end with when the new agent ran, or null to carry on as this agent. Nothing that goes
    // wrong here may stop the machine, because the agent and the console from the boot image still work.
    public async Task<int?> RunAsync(CancellationToken cancellationToken)
    {
        string? newAgent;

        try
        {
            AgentRelease? release = await CheckAsync(cancellationToken).ConfigureAwait(false);
            newAgent = release is null || string.Equals(release.Sha256, agent.Sha256, StringComparison.OrdinalIgnoreCase)
                ? null
                : await DownloadAsync(release, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return null;
        }
        catch (Exception exception)
        {
            log.Warning($"Cannot switch to the new agent ({exception.Message}). {EnvironmentTexts.OwnAgent(agent.Environment)}");
            newAgent = null;
        }

        string? console = await new ConsoleUpdate(server, log, agent).NewConsoleAsync(cancellationToken).ConfigureAwait(false);

        return newAgent is null && console is null ? null : await RelaunchAsync(newAgent, console).ConfigureAwait(false);
    }

    // Starts the new agent, or this one again with the new console, and waits for it to end.
    private async Task<int?> RelaunchAsync(string? newAgent, string? console)
    {
        string[] consoleArguments = console is null ? [] : [AgentOptions.ConsoleArgument, console];

        try
        {
            int exitCode = await relauncher.RunAsync(
                newAgent ?? agent.ExecutablePath ?? Environment.ProcessPath!,
                [.. agent.Arguments, .. consoleArguments, AgentOptions.NoUpdateArgument]).ConfigureAwait(false);

            // A code the agent never returns means the new one couldn't even start. The cause is a missing runtime or
            // DLL, a crash, or an option it doesn't know.
            if (exitCode is AgentExitCodes.ConfigurationError or < 0 or > AgentExitCodes.HighestAgentCode)
            {
                string code = EnvironmentTexts.ExitCode(agent.Environment, exitCode);
                log.Warning(newAgent is null
                    ? $"The agent could not run again with the new console (exit code {code}). Carrying on with the console from the boot image."
                    : $"The new agent could not run (exit code {code}). {EnvironmentTexts.OwnAgent(agent.Environment)}");

                return null;
            }

            return exitCode;
        }
        catch (Exception exception)
        {
            log.Warning(newAgent is null
                ? $"Cannot start the agent again with the new console ({exception.Message}). Carrying on with the console from the boot image."
                : $"Cannot switch to the new agent ({exception.Message}). {EnvironmentTexts.OwnAgent(agent.Environment)}");

            return null;
        }
    }

    // Waits for the server the way registration does. Any answer settles it, except a few refusals from a busy
    // server. If the server can't say what the current agent is, or not in the time it has, this agent carries on.
    private async Task<AgentRelease?> CheckAsync(CancellationToken cancellationToken)
    {
        long began = timeProvider.GetTimestamp();
        int failures = 0;
        int refusals = 0;

        while (!cancellationToken.IsCancellationRequested)
        {
            TimeSpan? left = Left(began);

            if (left <= TimeSpan.Zero)
            {
                return NotAnswered();
            }

            // The time that is left ends a try too, or a server that takes the call and says nothing would double it.
            using CancellationTokenSource timeUp = new(Timeout.InfiniteTimeSpan, timeProvider);
            using CancellationTokenSource trying = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeUp.Token);
            timeUp.CancelAfter(left ?? Timeout.InfiniteTimeSpan);

            try
            {
                AgentRelease? release = await server.GetReleaseAsync(trying.Token).ConfigureAwait(false);
                status?.Answered();

                return release;
            }
            catch (OperationCanceledException) when (timeUp.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
            {
                return NotAnswered();
            }
            catch (HttpRequestException exception) when (exception.StatusCode == HttpStatusCode.TooManyRequests && ++refusals <= MaxRefusals)
            {
                failures++;
                log.Warning("The server is busy. Asking again for the current agent shortly.");
            }
            catch (Exception exception) when (exception is HttpRequestException { StatusCode: null } or TimeoutException or TaskCanceledException
                && !cancellationToken.IsCancellationRequested)
            {
                failures++;
                log.Warning($"Cannot reach the server to ask for the current agent ({exception.Message}).");
                status?.Unreachable(exception);
            }
            catch (Exception exception) when (exception is HttpRequestException or JsonException)
            {
                log.Warning($"Cannot ask the server for the current agent ({exception.Message}). {EnvironmentTexts.OwnAgent(agent.Environment)}");

                return null;
            }

            await Task.Delay(Wait(failures, Left(began)), timeProvider, cancellationToken).ConfigureAwait(false);
        }

        return null;
    }

    // Null when the server has all the time it takes.
    private TimeSpan? Left(long began) => GiveUpAfter - timeProvider.GetElapsedTime(began);

    // No wait goes past the time that is left.
    private static TimeSpan Wait(int failures, TimeSpan? left)
    {
        TimeSpan delay = AgentLimits.RetryDelay(failures);

        return left is not { } rest || rest >= delay ? delay : rest > TimeSpan.Zero ? rest : TimeSpan.Zero;
    }

    private AgentRelease? NotAnswered()
    {
        log.Warning(string.Create(
            CultureInfo.InvariantCulture,
            $"The server did not answer within {GiveUpAfter?.TotalSeconds:0} seconds when asked for the current agent. " +
            $"{EnvironmentTexts.OwnAgent(agent.Environment)}"));

        return null;
    }

    private async Task<string> DownloadAsync(AgentRelease release, CancellationToken cancellationToken)
    {
        log.Information($"The server offers another agent, {release.Sha256[..12]}. Switching to it.");

        string path = Path.Combine(agent.Directory, EnvironmentTexts.DownloadedAgent(agent.Environment, release.Sha256));

        if (!await ReleaseFiles.HoldsAsync(path, release.Sha256, release.Size, cancellationToken).ConfigureAwait(false))
        {
            await ReleaseFiles.FetchAsync(path, release.Sha256, release.Size, server.DownloadReleaseAsync, cancellationToken).ConfigureAwait(false);
        }

        // Windows starts any file named .exe. Every other system starts a file only when its mode says it is a program.
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        return path;
    }
}
