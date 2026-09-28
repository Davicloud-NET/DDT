// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Net;
using System.Text.Json;
using DDT.Agent.Consoles;
using DDT.Contracts.Agents;

namespace DDT.Agent;

// Switches to the agent and the graphical console the server offers, so a DDT update never needs a new boot image. Only
// before the first registration: the new agent starts without this one's resume token, which would cost an approval.
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

    // The exit code to end with when the new agent ran, or null to carry on as this agent. Nothing that goes
    // wrong here may stop the machine: the agent and the console from the boot image still work.
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
            log.Warning($"Cannot switch to the new agent ({exception.Message}). Carrying on with the agent from the boot image.");
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

            // Codes the agent never returns mean the new one could not even start: a missing runtime or DLL, a
            // crash, or an option it does not know.
            if (exitCode is AgentExitCodes.ConfigurationError or < 0 or > AgentExitCodes.HighestAgentCode)
            {
                log.Warning(newAgent is null
                    ? $"The agent could not run again with the new console (exit code 0x{exitCode:X8}). Carrying on with the console from the boot image."
                    : $"The new agent could not run (exit code 0x{exitCode:X8}). Carrying on with the agent from the boot image.");

                return null;
            }

            return exitCode;
        }
        catch (Exception exception)
        {
            log.Warning(newAgent is null
                ? $"Cannot start the agent again with the new console ({exception.Message}). Carrying on with the console from the boot image."
                : $"Cannot switch to the new agent ({exception.Message}). Carrying on with the agent from the boot image.");

            return null;
        }
    }

    // Waits for the server like registration does. Any answer settles it, except a few refusals from a busy
    // server; a server that cannot say what the current agent is means carrying on with this one.
    private async Task<AgentRelease?> CheckAsync(CancellationToken cancellationToken)
    {
        int failures = 0;
        int refusals = 0;

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                AgentRelease? release = await server.GetReleaseAsync(cancellationToken).ConfigureAwait(false);
                status?.Answered();

                return release;
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
                log.Warning($"Cannot ask the server for the current agent ({exception.Message}). Carrying on with the agent from the boot image.");

                return null;
            }

            await Task.Delay(AgentLimits.RetryDelay(failures), timeProvider, cancellationToken).ConfigureAwait(false);
        }

        return null;
    }

    private async Task<string> DownloadAsync(AgentRelease release, CancellationToken cancellationToken)
    {
        log.Information($"The server offers another agent, {release.Sha256[..12]}. Switching to it.");

        string path = Path.Combine(agent.Directory, $"ddt-agent-{release.Sha256[..12].ToLowerInvariant()}.exe");

        if (!await ReleaseFiles.HoldsAsync(path, release.Sha256, release.Size, cancellationToken).ConfigureAwait(false))
        {
            await ReleaseFiles.FetchAsync(path, release.Sha256, release.Size, server.DownloadReleaseAsync, cancellationToken).ConfigureAwait(false);
        }

        return path;
    }
}
