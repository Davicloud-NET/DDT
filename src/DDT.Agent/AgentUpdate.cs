using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using DDT.Contracts.Agents;

namespace DDT.Agent;

// Switches to the agent the server offers, so a DDT update never needs a new boot image. It runs only before
// this agent first registers: the new agent starts without this one's resume token, so switching later would
// cost an approved machine its approval.
public sealed class AgentUpdate(
    IAgentServer server,
    IAgentRelauncher relauncher,
    AgentLog log,
    TimeProvider timeProvider,
    string currentSha256,
    string directory,
    IReadOnlyList<string> arguments)
{
    // Six retries wait 90 seconds in all, longer than the server's one minute window.
    private const int MaxRefusals = 6;

    public static async Task<string> Sha256Async(string path, CancellationToken cancellationToken)
    {
        await using FileStream file = new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);

        return Convert.ToHexStringLower(await SHA256.HashDataAsync(file, cancellationToken).ConfigureAwait(false));
    }

    // The exit code to end with when the new agent ran, or null to carry on as this agent. Nothing that goes
    // wrong here may stop the machine: the agent from the boot image still works.
    public async Task<int?> RunAsync(CancellationToken cancellationToken)
    {
        try
        {
            AgentRelease? release = await CheckAsync(cancellationToken).ConfigureAwait(false);

            if (release is null || string.Equals(release.Sha256, currentSha256, StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            log.Information($"The server offers another agent, {release.Sha256[..12]}. Switching to it.");

            string path = await DownloadAsync(release, cancellationToken).ConfigureAwait(false);
            int exitCode = await relauncher.RunAsync(path, [.. arguments, AgentOptions.NoUpdateArgument]).ConfigureAwait(false);

            // Codes the agent never returns mean the new one could not even start: a missing runtime or DLL, a
            // crash, or an option it does not know.
            if (exitCode is AgentExitCodes.ConfigurationError or < 0 or > AgentExitCodes.HighestAgentCode)
            {
                log.Warning($"The new agent could not run (exit code 0x{exitCode:X8}). Carrying on with the agent from the boot image.");

                return null;
            }

            return exitCode;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return null;
        }
        catch (Exception exception)
        {
            log.Warning($"Cannot switch to the new agent ({exception.Message}). Carrying on with the agent from the boot image.");

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
                return await server.GetReleaseAsync(cancellationToken).ConfigureAwait(false);
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
        string path = Path.Combine(directory, $"ddt-agent-{release.Sha256[..12].ToLowerInvariant()}.exe");

        if (File.Exists(path) && string.Equals(await Sha256Async(path, cancellationToken).ConfigureAwait(false), release.Sha256, StringComparison.OrdinalIgnoreCase))
        {
            return path;
        }

        string partial = path + ".part";

        await using (FileStream file = new(partial, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            await server.DownloadReleaseAsync(file, cancellationToken).ConfigureAwait(false);
        }

        // A truncated download or a file replaced on the server in between must never be started.
        if (new FileInfo(partial).Length != release.Size
            || !string.Equals(await Sha256Async(partial, cancellationToken).ConfigureAwait(false), release.Sha256, StringComparison.OrdinalIgnoreCase))
        {
            File.Delete(partial);

            throw new InvalidDataException("the download does not match what the server announced");
        }

        File.Move(partial, path, overwrite: true);

        return path;
    }
}
