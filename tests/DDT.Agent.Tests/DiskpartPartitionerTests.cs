using System.Security.Principal;
using DDT.Agent.Deployment;
using DDT.Contracts.Agents;
using Xunit;

namespace DDT.Agent.Tests;

public sealed class DiskpartPartitionerTests
{
    // Reads this PC's disks with the real IOCTLs and changes nothing. Opening a disk for its size needs an
    // elevated session, as in Windows PE.
    [Fact]
    [Trait("Category", "E2E")]
    public async Task FindsThisComputersInternalDisk()
    {
        using WindowsIdentity identity = WindowsIdentity.GetCurrent();
        Assert.SkipUnless(new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator), "Reading disk sizes needs an elevated session.");

        ImmediateTimeProvider time = new();
        AgentLog log = new(time, TextWriter.Null);
        DiskpartPartitioner partitioner = new(new ToolRunner(log, time), log, time, Path.GetTempPath());

        IReadOnlyList<LocalDisk> disks = await partitioner.ListDisksAsync(TestContext.Current.CancellationToken);

        Assert.NotEmpty(disks);
        Assert.All(disks, disk =>
        {
            Assert.True(disk.SizeBytes >= DiskEligibility.MinimumSizeBytes);
            Assert.True(Enum.IsDefined(disk.BusType) && disk.BusType != StorageBusType.Unknown);
        });

        ScriptedAgentServer server = new();

        while (log.QueuedLines > 0)
        {
            await log.FlushAsync(server, Guid.Empty, "token", TestContext.Current.CancellationToken);
        }

        TestContext.Current.SendDiagnosticMessage(string.Join(Environment.NewLine, server.SentLines.Select(line => line.Message)));
        Assert.Contains(server.SentLines, line => line.Level == AgentLogLevel.Information && line.Message.StartsWith("Disk 0:", StringComparison.Ordinal));
    }
}
