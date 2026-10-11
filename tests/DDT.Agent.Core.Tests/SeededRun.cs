// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Agent.Deployment;
using DDT.Agent.Sequences;
using DDT.Contracts.Sequences;
using DDT.Core.CloudInit;
using DDT.Core.Disks;
using DDT.Core.Sequences;
using Xunit;

namespace DDT.Agent.Tests;

// A run in the Linux boot environment up to its hand-over: the image and the seed, written by their steps to a disk
// in memory, and an agent file to put on the seed. A test changes the disk as a script between the two would.
internal sealed class SeededRun : IDisposable
{
    public static readonly WriteRawImageStep Write = new()
    {
        Id = Guid.Parse("0193a4b2-0000-7000-8000-0000000000d2"),
        Name = "Write the disk",
        ImageId = TestRawImage.ImageId,
    };

    public static readonly WriteCloudInitSeedStep Seed = new()
    {
        Id = Guid.Parse("0193a4b2-0000-7000-8000-0000000000d3"),
        Name = "Seed",
        MetaData = "instance-id: \"{{SmbiosUuid}}\"\nlocal-hostname: \"{{ComputerName}}\"\n",
        UserData = "#cloud-config\nusers:\n  - default\nruncmd:\n  - [touch, /etc/ddt-was-here]\n",
        NetworkConfig = "version: 2\n",
    };

    public static readonly RunScriptStep Script = new()
    {
        Id = Guid.Parse("0193a4b2-0000-7000-8000-0000000000d4"),
        Name = "In the installed Linux",
        Phase = SequencePhase.Linux,
        Interpreter = ScriptInterpreter.Shell,
        Script = "echo \"$DDT_PHASE\"",
        RebootExitCodes = [194],
    };

    private readonly TestRawImage _image = new();

    public SeededRun()
    {
        Run = new StepRunnerFixture([Write, Seed, Script], [_image.RunImage()]);
        _image.Serve(Run.Server);
        new Random(7).NextBytes(Agent);
        AgentPath = Path.Combine(Run.Tools.Root, "ddt-agent-0123456789ab");
        File.WriteAllBytes(AgentPath, Agent);
    }

    public StepRunnerFixture Run { get; }

    public byte[] Agent { get; } = new byte[300_000];

    public string AgentPath { get; }

    public MemoryRawDisk Disk => Run.RawDisks.Disks[0];

    // The seed as its step kept it, also after a hand-over put the run on it.
    public WrittenSeed Kept => Assert.IsType<WrittenSeed>(Run.Session.Seed);

    public LinuxHandOver HandOver(string? agentPath = null, IRawDisks? disks = null) =>
        new(disks ?? Run.RawDisks, agentPath ?? AgentPath, TestAgents.Configuration, Run.Log);

    // The image and its seed on the disk, as their steps leave them, and the run as it goes on in the installed Linux.
    public async Task<SequenceState> SeededAsync()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        IReadOnlyDictionary<string, string> image = (await Run.WriteRawImage.RunAsync(Write, Run.Context(SequencePhase.LinuxBoot), cancellationToken)).Outputs!;
        StepResult seeded = await Run.WriteCloudInitSeed.RunAsync(Seed, Run.Context(SequencePhase.LinuxBoot, image), cancellationToken);

        return SequenceStates.Start(StepRunnerFixture.RunId, Run.Session.Run.Sequence) with
        {
            Phase = SequencePhase.Linux,
            Steps = [new(Write.Id, StepState.Done, null, 1), new(Seed.Id, StepState.Done, null, 1), new(Script.Id, StepState.Pending, null)],
            Variables = new Dictionary<string, string>([.. image, .. seeded.Outputs!], StringComparer.Ordinal),
            Cursor = new NodeCursor(Script.Id, false),
        };
    }

    // The files the seed step wrote, for a test to change.
    public IReadOnlyList<SeedFile> Written() => CloudInitSeed.FilesOf(Kept.Files);

    // Puts a volume with these files in the seed's place, as a script that mounted the seed and changed it leaves it.
    public void RewriteSeed(IReadOnlyList<SeedFile> files, uint? serialNumber = null) =>
        PutInSeedsPlace(CloudInitSeed.Build(files, serialNumber ?? Kept.SerialNumber, Kept.Timestamp, Kept.FirstSector));

    public void PutInSeedsPlace(byte[] volume) => Disk.Write(Kept.FirstSector * GptLayout.SectorSize, volume);

    // The disk's partition table, read as the hand-over reads it.
    public GptLayout Table() => GptLayout.Read(Disk.ReadAt(0, GptLayout.MaxHeadBytes));

    public void Dispose() => Run.Dispose();
}
