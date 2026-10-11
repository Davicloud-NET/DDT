// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Text.Json;
using DDT.Agent.Deployment;
using DDT.Contracts.Sequences;
using DDT.Core.CloudInit;
using DDT.Core.Disks;

namespace DDT.Agent.Sequences;

// Hands a run over to the installed Linux on its cloud-init seed. The seed's volume is written again with this agent,
// its agent.json and the run's state and token beside the administrator's files, and with a vendor-data script that
// installs them when cloud-init runs it. Then the disk is put first, as at the end of a run that stays here.
public sealed class LinuxHandOver(IRawDisks disks, string agentPath, AgentConfiguration configuration, AgentLog log) : IHandOver
{
    public const string NoSeedMessage =
        "The run goes on in the installed Linux, but it wrote no cloud-init seed, which carries the agent there. Put a " +
        "cloud-init seed step before the first step in the installed Linux, then run the sequence again.";

    public const string NoTokenMessage =
        "The server gave this run no token for the installed Linux to go on with, so the run cannot be handed over.";

    public const string GoesOnMessage =
        "The run goes on once the installed Linux has started and cloud-init has run the agent's script from the seed. That " +
        "takes an image with cloud-init and systemd.";

    public static string TooLargeMessage(long shortBytes) =>
        $"The agent and the run's files are {ByteSize.Format(shortBytes)} too large for the cloud-init seed of " +
        $"{ByteSize.Format(CloudInitSeed.SizeBytes)}, so the run cannot go on in the installed Linux. Nothing was written to the seed.";

    public static string SeedGoneMessage(WrittenSeed seed, LocalDisk disk)
    {
        ArgumentNullException.ThrowIfNull(seed);
        ArgumentNullException.ThrowIfNull(disk);

        return $"The cloud-init seed is no longer partition {seed.PartitionNumber} of disk {disk.Number}, where this run wrote it, so the " +
            "agent cannot be put on it. Nothing was written to the disk.";
    }

    async Task IHandOver.HandOverAsync(SequenceRun run, RunBootOrder bootOrder, CancellationToken cancellationToken)
    {
        SequenceState handedOver = run.State with { Phase = SequencePhase.Linux };
        Stage(run.Session, handedOver);
        await bootOrder.PutRawImageFirstAsync(run, cancellationToken).ConfigureAwait(false);

        // Like the engine's saves, without the stop token. From here on the run's reports name the installed Linux.
        await run.Store.SaveAsync(handedOver, CancellationToken.None).ConfigureAwait(false);
    }

    void IHandOver.Withdraw(SequenceRun run) => Withdraw(run.Session);

    // Puts the agent and the run, as state says it goes on, onto the seed this run wrote. Nothing is written unless
    // all of it fits.
    public void Stage(RunSession session, SequenceState state)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(state);

        if (session is not { Seed: { } seed, Disk: { } disk })
        {
            throw new DeploymentStepException(NoSeedMessage);
        }

        string token = session.Tokens.RunToken ?? throw new DeploymentStepException(NoTokenMessage);
        byte[] agent = ReadAgent();
        IReadOnlyList<SeedFile> files = LinuxAgentFiles.ForSeed(
            agent,
            JsonSerializer.SerializeToUtf8Bytes(configuration, AgentConfigurationJsonContext.Default.AgentConfiguration),
            RunFiles.StateContent(state),
            RunFiles.TokenContent(token));
        long free = CloudInitSeed.FreeBytes(seed.Files, files);

        if (free < 0)
        {
            throw new DeploymentStepException(TooLargeMessage(-free));
        }

        using IRawDisk raw = disks.Open(disk);
        RequireSeed(raw, seed, disk);

        // Before the first byte, so a write that stops half way is taken back too.
        session.Seed = seed with { HoldsRun = true };
        Write(raw, seed, disk, CloudInitSeed.Build(seed.Files, seed.SerialNumber, seed.Timestamp, seed.FirstSector, files));

        log.Information(
            $"The agent ({ByteSize.Format(agent.Length)}), its settings and the run are on the cloud-init seed, partition " +
            $"{seed.PartitionNumber} of disk {disk.Number}, which has {ByteSize.Format(free)} left.");
        log.Information(GoesOnMessage);
    }

    // Writes the seed again as its step wrote it, so a run that failed leaves no token and no agent on the disk.
    // Never throws.
    public void Withdraw(RunSession session)
    {
        ArgumentNullException.ThrowIfNull(session);

        if (session is not { Seed: { HoldsRun: true } seed, Disk: { } disk })
        {
            return;
        }

        try
        {
            using IRawDisk raw = disks.Open(disk);
            raw.Write(seed.FirstSector * GptLayout.SectorSize, CloudInitSeed.Build(seed.Files, seed.SerialNumber, seed.Timestamp, seed.FirstSector));
            raw.Flush();
            session.Seed = seed with { HoldsRun = false };
            log.Information("The agent and the run's token are off the cloud-init seed again, which is as its step wrote it.");
        }
        catch (Exception exception) when (exception is DeploymentStepException or IOException or UnauthorizedAccessException)
        {
            log.Warning(
                $"The agent and the run's token could not be taken off the cloud-init seed of disk {disk.Number} again " +
                $"({exception.Message}). The server takes that token only for this run.");
        }
    }

    // The running agent, not the boot environment's, because it may have updated itself to a newer one.
    private byte[] ReadAgent()
    {
        try
        {
            return File.ReadAllBytes(agentPath);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new DeploymentStepException(
                $"The agent's own file {agentPath} could not be read to put it on the cloud-init seed ({exception.Message}).",
                exception);
        }
    }

    // A script may have changed the disk's partitions since the seed was written.
    private static void RequireSeed(IRawDisk raw, WrittenSeed seed, LocalDisk disk)
    {
        byte[] head = new byte[(int)Math.Min(RawDiskWriter.HeadBytes, raw.Length)];
        raw.Read(0, head);

        try
        {
            if (GptLayout.Read(head).Partitions.Any(partition =>
                partition.Number == seed.PartitionNumber
                && partition.FirstLba == seed.FirstSector
                && partition.Sectors == CloudInitSeed.SizeBytes / GptLayout.SectorSize))
            {
                return;
            }
        }
        catch (Exception exception) when (exception is InvalidGptException or ArgumentException)
        {
            throw new DeploymentStepException(SeedGoneMessage(seed, disk), exception);
        }

        throw new DeploymentStepException(SeedGoneMessage(seed, disk));
    }

    private static void Write(IRawDisk raw, WrittenSeed seed, LocalDisk disk, byte[] volume)
    {
        try
        {
            raw.Write(seed.FirstSector * GptLayout.SectorSize, volume);
            raw.Flush();
        }
        catch (Exception exception) when (exception is DeploymentStepException or IOException or UnauthorizedAccessException)
        {
            throw new DeploymentStepException(
                $"The agent could not be written to the cloud-init seed of disk {disk.Number}, so the run cannot go on in the " +
                $"installed Linux: {exception.Message}",
                exception);
        }
    }
}
