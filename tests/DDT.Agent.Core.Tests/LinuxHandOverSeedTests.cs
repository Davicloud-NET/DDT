// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Text;
using DDT.Agent.Deployment;
using DDT.Agent.Sequences;
using DDT.Contracts.Agents;
using DDT.Contracts.Sequences;
using DDT.Core.CloudInit;
using DDT.Core.Disks;
using DDT.Core.Sequences;
using Xunit;

namespace DDT.Agent.Tests;

// What the hand-over to the installed Linux puts on the seed a run wrote, read back from the disk, and what it leaves
// when it cannot. LinuxHandOverChangedSeedTests cover a seed that a later step changed.
public sealed class LinuxHandOverSeedTests : IDisposable
{
    private readonly SeededRun _seeded = new();

    public void Dispose() => _seeded.Dispose();

    private StepRunnerFixture Run => _seeded.Run;

    private MemoryRawDisk Disk => _seeded.Disk;

    private LinuxHandOver HandOver(string? agentPath = null) => _seeded.HandOver(agentPath);

    private Task<SequenceState> SeededAsync() => _seeded.SeededAsync();

    [Fact]
    public async Task TheSeedStepKeepsWhatItWroteForTheHandOver()
    {
        await SeededAsync();

        TestSeed seed = TestSeed.Read(Disk);
        WrittenSeed kept = Assert.IsType<WrittenSeed>(Run.Session.Seed);

        Assert.Equal((seed.Partition.Number, seed.Partition.FirstLba, seed.Volume.SerialNumber), (kept.PartitionNumber, kept.FirstSector, kept.SerialNumber));
        Assert.Equal(Run.Time.GetUtcNow().UtcDateTime, kept.Timestamp);
        Assert.False(kept.HoldsRun);
        Assert.Equal(
            (seed.Text(CloudInitSeed.MetaData), seed.Text(CloudInitSeed.UserData), seed.Text(CloudInitSeed.NetworkConfig)),
            (kept.Files.MetaData, kept.Files.UserData, kept.Files.NetworkConfig));
        Assert.Contains("local-hostname: \"PC-042\"", kept.Files.MetaData, StringComparison.Ordinal);
    }

    // The installed Linux asks a DNS for the server's name, which the boot environment had from the server itself.
    [Fact]
    public async Task TellsTheInstalledLinuxAtWhichAddressItReachedTheServer()
    {
        SequenceState state = await SeededAsync();
        System.Net.IPAddress? reached = null;
        LinuxHandOver handOver = new(Run.RawDisks, _seeded.AgentPath, TestAgents.Configuration, Run.Log) { ServerAddress = () => reached };
        reached = System.Net.IPAddress.Parse("10.77.0.1");

        handOver.Stage(Run.Session, state);

        Assert.Equal(
            """{"serverUrl":"https://ddt.example:8443/","rootCertificate":"-----BEGIN CERTIFICATE-----\nMIIB\n-----END CERTIFICATE-----\n","serverAddress":"10.77.0.1"}""",
            TestSeed.Read(Disk).Text(LinuxAgentFiles.SeedConfiguration));
    }

    [Fact]
    public async Task PutsTheAgentItsSettingsAndTheRunBesideTheAdministratorsFiles()
    {
        SequenceState state = await SeededAsync();
        TestSeed before = TestSeed.Read(Disk);
        int flushes = Disk.Flushes;

        HandOver().Stage(Run.Session, state);

        TestSeed after = TestSeed.Read(Disk);
        Assert.Equal(["meta-data", "user-data", "network-config", "vendor-data", "ddt"], after.Names());
        Assert.Equal(["ddt-agent", "agent.json", "state.json", "token"], after.Names(LinuxAgentFiles.SeedFolder));
        Assert.Equal(VendorDataScript.Bytes(), after.File(CloudInitSeed.VendorData));
        Assert.Equal(_seeded.Agent, after.File(LinuxAgentFiles.SeedAgent));
        Assert.Equal(
            """{"serverUrl":"https://ddt.example:8443/","rootCertificate":"-----BEGIN CERTIFICATE-----\nMIIB\n-----END CERTIFICATE-----\n"}""",
            after.Text(LinuxAgentFiles.SeedConfiguration));
        Assert.Equal(RunFiles.StateContent(state), after.File(LinuxAgentFiles.SeedState));
        Assert.Equal(StepRunnerFixture.RunToken, after.Text(LinuxAgentFiles.SeedToken));

        // The partition, the label and the serial number are the seed step's, and so is every byte of its files.
        Assert.Equal(before.Partition, after.Partition);
        Assert.Equal((before.Volume.Label, before.Volume.SerialNumber), (after.Volume.Label, after.Volume.SerialNumber));
        Assert.Equal("CIDATA", after.Volume.Label);

        foreach (string name in before.Names())
        {
            Assert.Equal(before.Volume.Find(name), after.Volume.Find(name));
            Assert.Equal(before.File(name), after.File(name));
        }

        Assert.Equal(flushes + 1, Disk.Flushes);
        Assert.Equal((after.Partition.FirstLba * GptLayout.SectorSize, (int)CloudInitSeed.SizeBytes), Disk.Writes[^1]);
        Assert.True(Run.Session.Seed?.HoldsRun);
    }

    // The agent there loads the run with the same code as in Windows, from where the script puts the two files.
    [Fact]
    public async Task TheStateAndTheTokenAreWhatTheAgentInTheInstalledLinuxLoads()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        SequenceState state = await SeededAsync();
        HandOver().Stage(Run.Session, state);
        TestSeed seed = TestSeed.Read(Disk);

        // What the script's install lines do, under a folder that stands in for /var/lib/ddt.
        string stateFolder = Path.Combine(Run.Tools.Root, "var-lib-ddt");
        Directory.CreateDirectory(Path.Combine(stateFolder, "run"));
        RunFiles files = new(stateFolder, Run.Log);
        await File.WriteAllBytesAsync(files.StatePath, seed.File(LinuxAgentFiles.SeedState), cancellationToken);
        await File.WriteAllBytesAsync(files.TokenPath, seed.File(LinuxAgentFiles.SeedToken), cancellationToken);

        SequenceState loaded = Assert.IsType<SequenceState>(await files.LoadStateAsync(cancellationToken));

        Assert.Equal((SequencePhase.Linux, StepRunnerFixture.RunId, SequenceState.TreeFormat), (loaded.Phase, loaded.RunId, loaded.Format));
        Assert.Equal(new NodeCursor(SeededRun.Script.Id, false), loaded.Cursor);
        Assert.Equal([StepState.Done, StepState.Done, StepState.Pending], loaded.Steps.Select(step => step.State));
        Assert.Equal(RunVariables.Set, loaded.Variables[RunVariables.RawImageWritten]);
        Assert.Equal(seed.Partition.Number.ToString(System.Globalization.CultureInfo.InvariantCulture), loaded.Variables[RunVariables.SeedPartition]);
        Assert.Equal(StepRunnerFixture.RunToken, await files.LoadTokenAsync(cancellationToken));
        Assert.Equal(
            (Path.Combine(stateFolder, "run", "state.json"), Path.Combine(stateFolder, "run", "token")),
            (files.StatePath, files.TokenPath));
        Assert.EndsWith("/run/state.json", LinuxAgentFiles.State, StringComparison.Ordinal);
        Assert.EndsWith("/run/token", LinuxAgentFiles.Token, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SaysThatTheAgentIsOnTheSeedHowLargeItIsAndWhenTheRunGoesOn()
    {
        SequenceState state = await SeededAsync();
        int before = (await Run.SentLinesAsync()).Count;

        HandOver().Stage(Run.Session, state);

        TestSeed seed = TestSeed.Read(Disk);
        List<AgentLogLine> lines = (await Run.SentLinesAsync())[before..];
        Assert.Equal(
            [
                $"The agent (293 KB), its settings and the run are on the cloud-init seed, partition 3 of disk 0, which has {ByteSize.Format(seed.Volume.FreeBytes)} left.",
                "The run goes on once the installed Linux has started and cloud-init has run the agent's script from the seed. That " +
                "takes an image with cloud-init and systemd.",
            ],
            lines.Select(line => line.Message));
        Assert.All(lines, line => Assert.Equal(AgentLogLevel.Information, line.Level));
        Assert.Equal("63 MB", ByteSize.Format(seed.Volume.FreeBytes));
        Assert.All(lines, line => Assert.DoesNotContain(StepRunnerFixture.RunToken, line.Message, StringComparison.Ordinal));
    }

    [Fact]
    public async Task RefusesAnAgentTooLargeForTheSeedBeforeAnythingIsWritten()
    {
        SequenceState state = await SeededAsync();
        string large = Path.Combine(Run.Tools.Root, "ddt-agent-large");
        await File.WriteAllBytesAsync(large, new byte[CloudInitSeed.SizeBytes], TestContext.Current.CancellationToken);
        TestSeed before = TestSeed.Read(Disk);
        int writes = Disk.Writes.Count;

        DeploymentStepException refusal = Assert.Throws<DeploymentStepException>(() => HandOver(large).Stage(Run.Session, state));

        Assert.Matches(
            @"^The agent and the run's files are \d+ KB too large for the cloud-init seed of 64 MB, so the run cannot go on in the installed Linux\. Nothing was written to the seed\.$",
            refusal.Message);
        Assert.Equal(writes, Disk.Writes.Count);
        Assert.Equal(before.Bytes, TestSeed.Read(Disk).Bytes);
        Assert.False(Run.Session.Seed?.HoldsRun);
    }

    // The validator asks for a seed before the first step in the installed Linux. A run without one still ends in words.
    [Fact]
    public async Task RefusesARunThatWroteNoSeed()
    {
        SequenceState state = await SeededAsync();
        int writes = Disk.Writes.Count;
        Run.Session.Seed = null;

        DeploymentStepException refusal = Assert.Throws<DeploymentStepException>(() => HandOver().Stage(Run.Session, state));

        Assert.Equal(LinuxHandOver.NoSeedMessage, refusal.Message);
        Assert.Equal(writes, Disk.Writes.Count);

        // Taking back what was never put there changes nothing either.
        Assert.False(HandOver().Withdraw(Run.Session, HandOverEnd.Failed));
        Assert.Equal(writes, Disk.Writes.Count);
    }

    [Fact]
    public async Task RefusesARunTheServerGaveNoToken()
    {
        SequenceState state = await SeededAsync();
        int writes = Disk.Writes.Count;
        RunSession session = new(StepRunnerFixture.MachineId, Run.Session.Run, new DeploymentTokens("session", "resume"))
        {
            Disk = Run.Session.Disk,
            Seed = Run.Session.Seed,
        };

        DeploymentStepException refusal = Assert.Throws<DeploymentStepException>(() => HandOver().Stage(session, state));

        Assert.Equal(LinuxHandOver.NoTokenMessage, refusal.Message);
        Assert.Equal(writes, Disk.Writes.Count);
    }

    [Fact]
    public async Task RefusesAnAgentFileItCannotRead()
    {
        SequenceState state = await SeededAsync();
        string missing = Path.Combine(Run.Tools.Root, "gone", "ddt-agent");

        DeploymentStepException refusal = Assert.Throws<DeploymentStepException>(() => HandOver(missing).Stage(Run.Session, state));

        Assert.StartsWith($"The agent's own file {missing} could not be read to put it on the cloud-init seed (", refusal.Message, StringComparison.Ordinal);
        Assert.False(TestSeed.Read(Disk).Holds(StepRunnerFixture.RunToken));
    }
}
