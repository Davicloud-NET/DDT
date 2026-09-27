// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Agent.Consoles;

// Puts the logo a registration names on the console at the machine: downloaded once into directory as
// console-logo-<hash>.png, checked against the hash, and shown by its path. In Windows PE directory is the agent's own;
// in the installed Windows it is the console's folder, which the account of DDT's session may read. A logo that cannot
// be downloaded leaves the console without one; the run goes on regardless.
public sealed class ConsoleLogo(IAgentServer server, ConsoleStatus status, string directory, AgentLog log)
{
    private string? _shown;

    public async Task ShowAsync(string? sha256, CancellationToken cancellationToken)
    {
        // The hash becomes part of a path, so only a hash is taken.
        if (sha256 is null || sha256.Length != 64 || !sha256.All(char.IsAsciiHexDigit) || !Directory.Exists(directory))
        {
            Show(null);

            return;
        }

        if (string.Equals(sha256, _shown, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        string path = Path.Combine(directory, $"console-logo-{sha256[..12].ToLowerInvariant()}.png");

        try
        {
            if (!await HoldsAsync(path, sha256, cancellationToken).ConfigureAwait(false))
            {
                string partial = path + ".part";

                await using (FileStream file = new(partial, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    await server.DownloadConsoleLogoAsync(file, cancellationToken).ConfigureAwait(false);
                }

                if (!await HoldsAsync(partial, sha256, cancellationToken).ConfigureAwait(false))
                {
                    File.Delete(partial);

                    throw new InvalidDataException("the download does not match what the server announced");
                }

                File.Move(partial, path, overwrite: true);
            }

            Show(path);
            _shown = sha256;
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            log.Warning($"Cannot show the logo the server has for the console ({exception.Message}).");
        }
    }

    private void Show(string? path)
    {
        _shown = null;
        status.SetLogo(path);
    }

    private static async Task<bool> HoldsAsync(string path, string sha256, CancellationToken cancellationToken) =>
        File.Exists(path)
        && string.Equals(await AgentUpdate.Sha256Async(path, cancellationToken).ConfigureAwait(false), sha256, StringComparison.OrdinalIgnoreCase);
}
