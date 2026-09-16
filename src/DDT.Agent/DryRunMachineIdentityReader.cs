using System.Security.Cryptography;
using System.Text;

namespace DDT.Agent;

// A stable identity per dry run id, so several dry runs can stand in for several machines and a
// repeated run is recognised as the same machine.
public sealed class DryRunMachineIdentityReader(int dryRunId) : IMachineIdentityReader
{
    public MachineIdentity Read()
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes($"ddt-dry-run-{dryRunId}"));

        // 02 marks a locally administered unicast MAC, which no real network card carries.
        string mac = $"02DD{Convert.ToHexString(hash, 16, 4)}";

        return new MachineIdentity(
            new Guid(hash.AsSpan(0, 16)).ToString("D"),
            mac,
            [mac],
            "DDT",
            "Dry run",
            $"DRYRUN-{dryRunId}");
    }
}
