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
// when it cannot.
public sealed class LinuxHandOverSeedTests : IDisposable
{
    private static readonly WriteRawImageStep s_write = new()
    {
        Id = Guid.Parse("0193a4b2-0000-7000-8000-0000000000d2"),
        Name = "Write the disk",
        ImageId = TestRawImage.ImageId,
    };

    private static readonly WriteCloudInitSeedStep s_seed = new()
    {
        Id = Guid.Parse("0193a4b2-0000-7000-8000-0000000000d3"),
        Name = "Seed",
        MetaData = "instance-id: \"{{SmbiosUuid}}\"\nlocal-hostname: \"{{ComputerName}}\"\n",
        UserData = "#cloud-config\nusers:\n  - default\nruncmd:\n  - [touch, /etc/ddt-was-here]\n",
        NetworkConfig = "version: 2\n",
    };

    private static readonly RunScriptStep s_script = new()
    {
        Id = Guid.Parse("0193a4b2-0000-7000-8000-0000000000d4"),
        Name = "In the installed Linux",
        Phase = SequencePhase.Linux,
        Interpreter = ScriptInterpreter.Shell,
        Script = "echo \"$DDT_PHASE\"",
        RebootExitCodes = [194],
    };

    private readonly TestRawImage _image = new();
    private readonly StepRunnerFixture _run;
    private readonly byte[] _agent = new byte[300_000];
    private readonly string _agentPath;

    public LinuxHandOverSeedTests()
    {
        _run = new StepRunnerFixture([s_write, s_seed, s_script], [_image.RunImage()]);
        _image.Serve(_run.Server);
        new Random(7).NextBytes(_agent);
        _agentPath = Path.Combine(_run.Tools.Root, "ddt-agent-0123456789ab");
        File.WriteAllBytes(_agentPath, _agent);
    }

    public void Dispose() => _run.Dispose();

    private MemoryRawDisk Disk => _run.RawDisks.Disks[0];

    private LinuxHandOver HandOver(string? agentPath = null) => new(_run.RawDisks, agentPath ?? _agentPath, TestAgents.Configuration, _run.Log);

    // The image and its seed on the disk, as their steps leave them, and the run as it goes on in the installed Linux.
    private async Task<SequenceState> SeededAsync()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        IReadOnlyDictionary<string, string> image = (await _run.WriteRawImage.RunAsync(s_write, _run.Context(SequencePhase.LinuxBoot), cancellationToken)).Outputs!;
        StepResult seeded = await _run.WriteCloudInitSeed.RunAsync(s_seed, _run.Context(SequencePhase.LinuxBoot, image), cancellationToken);

        return SequenceStates.Start(StepRunnerFixture.RunId, _run.Session.Run.Sequence) with
        {
            Phase = SequencePhase.Linux,
            Steps = [new(s_write.Id, StepState.Done, null, 1), new(s_seed.Id, StepState.Done, null, 1), new(s_script.Id, StepState.Pending, null)],
            Variables = new Dictionary<string, string>([.. image, .. seeded.Outputs!], StringComparer.Ordinal),
            Cursor = new NodeCursor(s_script.Id, false),
        };
    }

    [Fact]
    public async Task TheSeedStepKeepsWhatItWroteForTheHandOver()
    {
        await SeededAsync();

        TestSeed seed = TestSeed.Read(Disk);
        WrittenSeed kept = Assert.IsType<WrittenSeed>(_run.Session.Seed);

        Assert.Equal((seed.Partition.Number, seed.Partition.FirstLba, seed.Volume.SerialNumber), (kept.PartitionNumber, kept.FirstSector, kept.SerialNumber));
        Assert.Equal(_run.Time.GetUtcNow().UtcDateTime, kept.Timestamp);
        Assert.False(kept.HoldsRun);
        Assert.Equal(
            (seed.Text(CloudInitSeed.MetaData), seed.Text(CloudInitSeed.UserData), seed.Text(CloudInitSeed.NetworkConfig)),
            (kept.Files.MetaData, kept.Files.UserData, kept.Files.NetworkConfig));
        Assert.Contains("local-hostname: \"PC-042\"", kept.Files.MetaData, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PutsTheAgentItsSettingsAndTheRunBesideTheAdministratorsFiles()
    {
        SequenceState state = await SeededAsync();
        TestSeed before = TestSeed.Read(Disk);
        int flushes = Disk.Flushes;

        HandOver().Stage(_run.Session, state);

        TestSeed after = TestSeed.Read(Disk);
        Assert.Equal(["meta-data", "user-data", "network-config", "vendor-data", "ddt"], after.Names());
        Assert.Equal(["ddt-agent", "agent.json", "state.json", "token"], after.Names(LinuxAgentFiles.SeedFolder));
        Assert.Equal(VendorDataScript.Bytes(), after.File(CloudInitSeed.VendorData));
        Assert.Equal(_agent, after.File(LinuxAgentFiles.SeedAgent));
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
        Assert.True(_run.Session.Seed?.HoldsRun);
    }

    // The agent there loads the run with the same code as in Windows, from where the script puts the two files.
    [Fact]
    public async Task TheStateAndTheTokenAreWhatTheAgentInTheInstalledLinuxLoads()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        SequenceState state = await SeededAsync();
        HandOver().Stage(_run.Session, state);
        TestSeed seed = TestSeed.Read(Disk);

        // What the script's install lines do, under a folder that stands in for /var/lib/ddt.
        string stateFolder = Path.Combine(_run.Tools.Root, "var-lib-ddt");
        Directory.CreateDirectory(Path.Combine(stateFolder, "run"));
        RunFiles files = new(stateFolder, _run.Log);
        await File.WriteAllBytesAsync(files.StatePath, seed.File(LinuxAgentFiles.SeedState), cancellationToken);
        await File.WriteAllBytesAsync(files.TokenPath, seed.File(LinuxAgentFiles.SeedToken), cancellationToken);

        SequenceState loaded = Assert.IsType<SequenceState>(await files.LoadStateAsync(cancellationToken));

        Assert.Equal((SequencePhase.Linux, StepRunnerFixture.RunId, SequenceState.TreeFormat), (loaded.Phase, loaded.RunId, loaded.Format));
        Assert.Equal(new NodeCursor(s_script.Id, false), loaded.Cursor);
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
        int before = (await _run.SentLinesAsync()).Count;

        HandOver().Stage(_run.Session, state);

        TestSeed seed = TestSeed.Read(Disk);
        List<AgentLogLine> lines = (await _run.SentLinesAsync())[before..];
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
        string large = Path.Combine(_run.Tools.Root, "ddt-agent-large");
        await File.WriteAllBytesAsync(large, new byte[CloudInitSeed.SizeBytes], TestContext.Current.CancellationToken);
        TestSeed before = TestSeed.Read(Disk);
        int writes = Disk.Writes.Count;

        DeploymentStepException refusal = Assert.Throws<DeploymentStepException>(() => HandOver(large).Stage(_run.Session, state));

        Assert.Matches(
            @"^The agent and the run's files are \d+ KB too large for the cloud-init seed of 64 MB, so the run cannot go on in the installed Linux\. Nothing was written to the seed\.$",
            refusal.Message);
        Assert.Equal(writes, Disk.Writes.Count);
        Assert.Equal(before.Bytes, TestSeed.Read(Disk).Bytes);
        Assert.False(_run.Session.Seed?.HoldsRun);
    }

    // The validator asks for a seed before the first step in the installed Linux. A run without one still ends in words.
    [Fact]
    public async Task RefusesARunThatWroteNoSeed()
    {
        SequenceState state = await SeededAsync();
        int writes = Disk.Writes.Count;
        _run.Session.Seed = null;

        DeploymentStepException refusal = Assert.Throws<DeploymentStepException>(() => HandOver().Stage(_run.Session, state));

        Assert.Equal(LinuxHandOver.NoSeedMessage, refusal.Message);
        Assert.Equal(writes, Disk.Writes.Count);

        // Taking back what was never put there changes nothing either.
        HandOver().Withdraw(_run.Session);
        Assert.Equal(writes, Disk.Writes.Count);
    }

    [Fact]
    public async Task RefusesARunTheServerGaveNoToken()
    {
        SequenceState state = await SeededAsync();
        int writes = Disk.Writes.Count;
        RunSession session = new(StepRunnerFixture.MachineId, _run.Session.Run, new DeploymentTokens("session", "resume"))
        {
            Disk = _run.Session.Disk,
            Seed = _run.Session.Seed,
        };

        DeploymentStepException refusal = Assert.Throws<DeploymentStepException>(() => HandOver().Stage(session, state));

        Assert.Equal(LinuxHandOver.NoTokenMessage, refusal.Message);
        Assert.Equal(writes, Disk.Writes.Count);
    }

    [Fact]
    public async Task RefusesAnAgentFileItCannotRead()
    {
        SequenceState state = await SeededAsync();
        string missing = Path.Combine(_run.Tools.Root, "gone", "ddt-agent");

        DeploymentStepException refusal = Assert.Throws<DeploymentStepException>(() => HandOver(missing).Stage(_run.Session, state));

        Assert.StartsWith($"The agent's own file {missing} could not be read to put it on the cloud-init seed (", refusal.Message, StringComparison.Ordinal);
        Assert.False(TestSeed.Read(Disk).Holds(StepRunnerFixture.RunToken));
    }

    // A script between the seed and the hand-over may have changed the partitions.
    [Fact]
    public async Task RefusesASeedThatIsNoLongerWhereTheRunWroteIt()
    {
        SequenceState state = await SeededAsync();
        WrittenSeed seed = Assert.IsType<WrittenSeed>(_run.Session.Seed);
        _run.Session.Seed = seed with { FirstSector = seed.FirstSector - GptLayout.AlignmentSectors };
        int writes = Disk.Writes.Count;

        DeploymentStepException refusal = Assert.Throws<DeploymentStepException>(() => HandOver().Stage(_run.Session, state));

        Assert.Equal(
            "The cloud-init seed is no longer partition 3 of disk 0, where this run wrote it, so the agent cannot be put on it. " +
            "Nothing was written to the disk.",
            refusal.Message);
        Assert.Equal(writes, Disk.Writes.Count);
    }

    [Fact]
    public async Task AWriteThatStopsHalfWaySaysSoAndIsTakenBack()
    {
        SequenceState state = await SeededAsync();
        TestSeed before = TestSeed.Read(Disk);
        LinuxHandOver handOver = HandOver();
        Disk.FailNextWriteAfter = 4 * 1024 * 1024;

        DeploymentStepException failure = Assert.Throws<DeploymentStepException>(() => handOver.Stage(_run.Session, state));

        Assert.Equal(
            "The agent could not be written to the cloud-init seed of disk 0, so the run cannot go on in the installed Linux: " +
            "The device is not ready.",
            failure.Message);
        Assert.True(_run.Session.Seed?.HoldsRun);
        Assert.True(TestSeed.Read(Disk).Holds(StepRunnerFixture.RunToken));

        handOver.Withdraw(_run.Session);

        TestSeed after = TestSeed.Read(Disk);
        Assert.Equal(before.Bytes, after.Bytes);
        Assert.Equal(["meta-data", "user-data", "network-config"], after.Names());
        Assert.False(after.Holds(StepRunnerFixture.RunToken));
        Assert.False(_run.Session.Seed?.HoldsRun);
        Assert.Equal(
            "The agent and the run's token are off the cloud-init seed again, which is as its step wrote it.",
            (await _run.SentLinesAsync())[^1].Message);
    }

    // The disk no longer takes a write. The warning says where the token still is, and never the token.
    [Fact]
    public async Task WarnsWhenTheTokenCannotBeTakenOffTheSeedAgain()
    {
        SequenceState state = await SeededAsync();
        LinuxHandOver handOver = HandOver();
        handOver.Stage(_run.Session, state);
        Disk.FailNextWriteAfter = 0;

        handOver.Withdraw(_run.Session);

        AgentLogLine last = (await _run.SentLinesAsync())[^1];
        Assert.Equal(
            (AgentLogLevel.Warning,
                "The agent and the run's token could not be taken off the cloud-init seed of disk 0 again (The device is not ready.). " +
                "The server takes that token only for this run."),
            (last.Level, last.Message));
        Assert.True(_run.Session.Seed?.HoldsRun);
        Assert.Equal(StepRunnerFixture.RunToken, Encoding.ASCII.GetString(TestSeed.Read(Disk).File(LinuxAgentFiles.SeedToken)));
    }
}
