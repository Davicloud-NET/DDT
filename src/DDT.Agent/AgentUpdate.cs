// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using DDT.Agent.Consoles;
using DDT.ConsoleProtocol;
using DDT.Contracts.Agents;

namespace DDT.Agent;

// Switches to the agent and the graphical console the server offers, so a DDT update never needs a new boot image. It
// runs only before this agent first registers: the new agent starts without this one's resume token, so switching later
// would cost an approved machine its approval. status shows a server that cannot be reached on the console.
// consolePath is the console this agent started from beside itself, which the server's console replaces; null when
// there is none, or when --console named one, which stays. agentPath is this agent, which starts again for a new
// console alone.
public sealed class AgentUpdate(
    IAgentServer server,
    IAgentRelauncher relauncher,
    AgentLog log,
    TimeProvider timeProvider,
    string currentSha256,
    string directory,
    IReadOnlyList<string> arguments,
    ConsoleStatus? status = null,
    string? consolePath = null,
    string? agentPath = null)
{
    // Six retries wait 90 seconds in all, longer than the server's one minute window.
    private const int MaxRefusals = 6;

    public static async Task<string> Sha256Async(string path, CancellationToken cancellationToken)
    {
        await using FileStream file = new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);

        return Convert.ToHexStringLower(await SHA256.HashDataAsync(file, cancellationToken).ConfigureAwait(false));
    }

    // The exit code to end with when the new agent ran, or null to carry on as this agent. Nothing that goes
    // wrong here may stop the machine: the agent and the console from the boot image still work.
    public async Task<int?> RunAsync(CancellationToken cancellationToken)
    {
        string? agent;

        try
        {
            AgentRelease? release = await CheckAsync(cancellationToken).ConfigureAwait(false);
            agent = release is null || string.Equals(release.Sha256, currentSha256, StringComparison.OrdinalIgnoreCase)
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
            agent = null;
        }

        string? console = await NewConsoleAsync(cancellationToken).ConfigureAwait(false);

        if (agent is null && console is null)
        {
            return null;
        }

        string[] consoleArguments = console is null ? [] : [AgentOptions.ConsoleArgument, console];

        try
        {
            int exitCode = await relauncher.RunAsync(
                agent ?? agentPath ?? Environment.ProcessPath!,
                [.. arguments, .. consoleArguments, AgentOptions.NoUpdateArgument]).ConfigureAwait(false);

            // Codes the agent never returns mean the new one could not even start: a missing runtime or DLL, a
            // crash, or an option it does not know.
            if (exitCode is AgentExitCodes.ConfigurationError or < 0 or > AgentExitCodes.HighestAgentCode)
            {
                log.Warning(agent is null
                    ? $"The agent could not run again with the new console (exit code 0x{exitCode:X8}). Carrying on with the console from the boot image."
                    : $"The new agent could not run (exit code 0x{exitCode:X8}). Carrying on with the agent from the boot image.");

                return null;
            }

            return exitCode;
        }
        catch (Exception exception)
        {
            log.Warning(agent is null
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

        string path = Path.Combine(directory, $"ddt-agent-{release.Sha256[..12].ToLowerInvariant()}.exe");

        if (!await HoldsAsync(path, release.Sha256, release.Size, cancellationToken).ConfigureAwait(false))
        {
            await FetchAsync(path, release.Sha256, release.Size, server.DownloadReleaseAsync, cancellationToken).ConfigureAwait(false);
        }

        return path;
    }

    // The console the server offers in place of the one beside this agent, in a folder of its own next to the agent,
    // or null to keep that one.
    private async Task<string?> NewConsoleAsync(CancellationToken cancellationToken)
    {
        if (consolePath is null)
        {
            return null;
        }

        try
        {
            ConsoleRelease? release = await server.GetConsoleReleaseAsync(cancellationToken).ConfigureAwait(false);

            if (release is null)
            {
                return null;
            }

            // The names become paths, so only the console's own are taken.
            if (!IsConsole(release))
            {
                throw new InvalidDataException("the server names other files than the console's");
            }

            if (await HoldsAsync(Path.GetDirectoryName(Path.GetFullPath(consolePath))!, release, cancellationToken).ConfigureAwait(false))
            {
                return null;
            }

            string id = release.Files.Single(file => file.Name == ConsolePipe.FileName).Sha256[..12].ToLowerInvariant();
            string folder = Path.Combine(directory, $"console-{id}");
            log.Information($"The server offers another console, {id}. Switching to it.");

            if (!await HoldsAsync(folder, release, cancellationToken).ConfigureAwait(false))
            {
                Directory.CreateDirectory(folder);

                foreach (ConsoleReleaseFile file in release.Files)
                {
                    await FetchAsync(
                        Path.Combine(folder, file.Name),
                        file.Sha256,
                        file.Size,
                        (destination, token) => server.DownloadConsoleFileAsync(file.Name, destination, token),
                        cancellationToken).ConfigureAwait(false);
                }
            }

            return Path.Combine(folder, ConsolePipe.FileName);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return null;
        }
        catch (Exception exception)
        {
            log.Warning($"Cannot switch to the console the server offers ({exception.Message}). Carrying on with the console from the boot image.");

            return null;
        }
    }

    private static bool IsConsole(ConsoleRelease release) =>
        release.Files.Count == ConsolePipe.Files.Count
        && ConsolePipe.Files.All(name => release.Files.Count(file => file.Name == name) == 1)
        && release.Files.All(file => file.Size >= 0 && file.Sha256.Length == 64 && file.Sha256.All(char.IsAsciiHexDigit));

    private static async Task<bool> HoldsAsync(string folder, ConsoleRelease release, CancellationToken cancellationToken)
    {
        foreach (ConsoleReleaseFile file in release.Files)
        {
            if (!await HoldsAsync(Path.Combine(folder, file.Name), file.Sha256, file.Size, cancellationToken).ConfigureAwait(false))
            {
                return false;
            }
        }

        return true;
    }

    private static async Task<bool> HoldsAsync(string path, string sha256, long size, CancellationToken cancellationToken) =>
        File.Exists(path)
        && new FileInfo(path).Length == size
        && string.Equals(await Sha256Async(path, cancellationToken).ConfigureAwait(false), sha256, StringComparison.OrdinalIgnoreCase);

    private static async Task FetchAsync(
        string path,
        string sha256,
        long size,
        Func<Stream, CancellationToken, Task> download,
        CancellationToken cancellationToken)
    {
        string partial = path + ".part";

        await using (FileStream file = new(partial, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            await download(file, cancellationToken).ConfigureAwait(false);
        }

        // A truncated download or a file replaced on the server in between must never be started.
        if (!await HoldsAsync(partial, sha256, size, cancellationToken).ConfigureAwait(false))
        {
            File.Delete(partial);

            throw new InvalidDataException("the download does not match what the server announced");
        }

        File.Move(partial, path, overwrite: true);
    }
}
