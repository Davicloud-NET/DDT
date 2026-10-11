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
// its agent.json and the run's state and token beside the files it holds, and with a vendor-data script that installs
// them when cloud-init runs it. Then the disk is put first, as at the end of a run that stays here.
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

    public const string WithdrawnMessage =
        "The agent and the run's token are off the cloud-init seed again, which is as it was before the hand-over.";

    public static string TooLargeMessage(long shortBytes) =>
        $"The agent and the run's files are {ByteSize.Format(shortBytes)} too large for the cloud-init seed of " +
        $"{ByteSize.Format(CloudInitSeed.SizeBytes)}, so the run cannot go on in the installed Linux. Nothing was written to the seed.";

    public static string ChangedMessage(string changes) =>
        $"A step changed the cloud-init seed after it was written: {changes}. The seed keeps the files it holds now, and the " +
        "agent goes beside them.";

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

        using IRawDisk raw = disks.Open(disk);

        // What the disk holds, not what the step wrote: a script may have changed the seed's files since, on purpose.
        SeedVolume found = seed.Found ?? SeedOnDisk.Read(raw, seed, disk);
        IReadOnlyList<SeedFile> files =
        [
            .. found.Files,
            .. LinuxAgentFiles.ForSeed(
                agent,
                JsonSerializer.SerializeToUtf8Bytes(configuration, AgentConfigurationJsonContext.Default.AgentConfiguration),
                RunFiles.StateContent(state),
                RunFiles.TokenContent(token)),
        ];
        (byte[] volume, long free) = Build(files, found, seed, disk);

        if (found.ChangesSince(CloudInitSeed.FilesOf(seed.Files)) is { } changes)
        {
            log.Information(ChangedMessage(changes));
        }

        // Before the first byte, so a write that stops half way is taken back too.
        session.Seed = seed with { Found = found };
        Write(raw, seed, disk, volume);

        log.Information(
            $"The agent ({ByteSize.Format(agent.Length)}), its settings and the run are on the cloud-init seed, partition " +
            $"{seed.PartitionNumber} of disk {disk.Number}, which has {ByteSize.Format(free)} left.");
        log.Information(GoesOnMessage);
    }

    // Writes the seed again as the hand-over found it, so a run that failed leaves no token and no agent on the disk.
    // Never throws.
    public void Withdraw(RunSession session)
    {
        ArgumentNullException.ThrowIfNull(session);

        if (session is not { Seed: { Found: { } found } seed, Disk: { } disk })
        {
            return;
        }

        try
        {
            byte[] volume = CloudInitSeed.Build(found.Files, found.SerialNumber, seed.Timestamp, seed.FirstSector);

            using IRawDisk raw = disks.Open(disk);
            raw.Write(seed.FirstSector * GptLayout.SectorSize, volume);
            raw.Flush();
            session.Seed = seed with { Found = null };
            log.Information(WithdrawnMessage);
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

    // The volume with what it has left. It keeps the serial number it has on the disk, by which the installed Linux
    // knows it. The builder refuses what a damaged volume may hold, such as one name twice.
    private static (byte[] Volume, long Free) Build(IReadOnlyList<SeedFile> files, SeedVolume found, WrittenSeed seed, LocalDisk disk)
    {
        long free;

        try
        {
            free = CloudInitSeed.FreeBytes(files);

            if (free >= 0)
            {
                return (CloudInitSeed.Build(files, found.SerialNumber, seed.Timestamp, seed.FirstSector), free);
            }
        }
        catch (Exception exception) when (exception is InvalidOperationException or ArgumentException)
        {
            throw new DeploymentStepException(
                $"The cloud-init seed of disk {disk.Number} cannot be written again with the files it holds now ({exception.Message}), " +
                "so the agent cannot be put on it. Nothing was written to the disk.",
                exception);
        }

        throw new DeploymentStepException(TooLargeMessage(-free));
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
