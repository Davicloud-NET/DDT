using System.Security.Cryptography;
using DDT.Contracts.Agents;
using DDT.Server.Configuration;
using Microsoft.Extensions.Options;

namespace DDT.Server.Machines;

// Hashing the agent on every check would read it every time a machine boots, so the hash is kept until the
// file's length or write time changes, which is also how replacing the file is noticed without a restart.
public sealed class AgentReleaseStore(IOptions<AgentReleaseOptions> options, IOptions<DdtOptions> ddt)
{
    private readonly Lock _lock = new();
    private (long Length, DateTime WriteTimeUtc, AgentRelease Release)? _cached;

    public string BinaryPath => Path.GetFullPath(string.IsNullOrWhiteSpace(options.Value.BinaryPath)
        ? Path.Combine(ddt.Value.StorePath, "agent", "ddt-agent.exe")
        : options.Value.BinaryPath);

    public async Task<AgentRelease?> CurrentAsync(CancellationToken cancellationToken)
    {
        FileInfo file = new(BinaryPath);

        if (!file.Exists)
        {
            return null;
        }

        lock (_lock)
        {
            if (_cached is { } cached && cached.Length == file.Length && cached.WriteTimeUtc == file.LastWriteTimeUtc)
            {
                return cached.Release;
            }
        }

        byte[] hash;

        await using (FileStream stream = file.OpenRead())
        {
            hash = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
        }

        AgentRelease release = new(Convert.ToHexStringLower(hash), file.Length);

        lock (_lock)
        {
            _cached = (file.Length, file.LastWriteTimeUtc, release);
        }

        return release;
    }
}
