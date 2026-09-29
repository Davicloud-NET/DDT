// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.ConsoleProtocol;
using DDT.Contracts.Agents;

namespace DDT.Agent;

// Downloads the graphical console the server offers into a separate folder. It's used instead of the console next to
// the agent.
internal sealed class ConsoleUpdate(IAgentServer server, AgentLog log, RunningAgent agent)
{
    // Returns the new console's executable, or null to keep the current one. Nothing that goes wrong here stops the
    // machine.
    public async Task<string?> NewConsoleAsync(CancellationToken cancellationToken)
    {
        if (agent.ConsolePath is not { } consolePath)
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

            // The names become paths, so only the console's known file names are accepted.
            if (!IsConsole(release))
            {
                throw new InvalidDataException("the server names other files than the console's");
            }

            if (await HoldsAsync(Path.GetDirectoryName(Path.GetFullPath(consolePath))!, release, cancellationToken).ConfigureAwait(false))
            {
                return null;
            }

            return await FetchAsync(release, cancellationToken).ConfigureAwait(false);
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
            if (!await ReleaseFiles.HoldsAsync(Path.Combine(folder, file.Name), file.Sha256, file.Size, cancellationToken).ConfigureAwait(false))
            {
                return false;
            }
        }

        return true;
    }

    private async Task<string> FetchAsync(ConsoleRelease release, CancellationToken cancellationToken)
    {
        string id = release.Files.Single(file => file.Name == ConsolePipe.FileName).Sha256[..12].ToLowerInvariant();
        string folder = Path.Combine(agent.Directory, $"console-{id}");
        log.Information($"The server offers another console, {id}. Switching to it.");

        if (!await HoldsAsync(folder, release, cancellationToken).ConfigureAwait(false))
        {
            Directory.CreateDirectory(folder);

            foreach (ConsoleReleaseFile file in release.Files)
            {
                await ReleaseFiles.FetchAsync(
                    Path.Combine(folder, file.Name),
                    file.Sha256,
                    file.Size,
                    (destination, token) => server.DownloadConsoleFileAsync(file.Name, destination, token),
                    cancellationToken).ConfigureAwait(false);
            }
        }

        return Path.Combine(folder, ConsolePipe.FileName);
    }
}
