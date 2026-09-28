// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Security.Cryptography;
using DDT.Agent.Deployment;
using DDT.Contracts.Sequences;
using DDT.Core.CloudInit;
using DDT.Core.Disks;
using DDT.Core.Sequences;

namespace DDT.Agent.Sequences;

// Adds a partition at the end of the disk a raw disk image was written to in this run, with a FAT volume labelled
// CIDATA that holds the seed files filled in with this machine's values, where cloud-init's NoCloud data source finds
// them at the machine's first start. The partition goes at the end so that cloud-init can grow the image's last
// partition into the space between. The volume is written first and the partition table last.
public sealed class WriteCloudInitSeedStepRunner(IRawDisks disks, RunSession session, AgentLog log, TimeProvider timeProvider)
{
    public const string NoImageMessage =
        "The cloud-init seed goes onto the disk a raw disk image was written to in this run, and none was written.";

    public Task<StepResult> RunAsync(WriteCloudInitSeedStep step, StepContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(step);
        ArgumentNullException.ThrowIfNull(context);

        if (!context.Variables.TryGetValue(RunVariables.RawImageWritten, out string? written) || written != RunVariables.Set)
        {
            throw new DeploymentStepException(NoImageMessage);
        }

        LocalDisk disk = session.Disk ?? throw new DeploymentStepException(NoImageMessage);
        (string metaData, string userData, string? networkConfig) = Render(step, context.Machine.Value(MachineVariableNames.ComputerName), context.Machine);

        using IRawDisk raw = disks.Open(disk);
        byte[] head = new byte[(int)Math.Min(RawDiskWriter.HeadBytes, raw.Length)];
        raw.Read(0, head);

        GptLayout layout;

        try
        {
            layout = GptLayout.Read(head).WithPartitionAtEnd(
                GptPartitionTypes.BasicData,
                Guid.NewGuid(),
                CloudInitSeed.Label,
                CloudInitSeed.SizeBytes / GptLayout.SectorSize);
        }
        catch (Exception exception) when (exception is InvalidGptException or InvalidOperationException or ArgumentException)
        {
            throw new DeploymentStepException($"The cloud-init seed cannot be added to disk {disk.Number}: {exception.Message}", exception);
        }

        GptPartition seed = layout.Partitions.MaxBy(partition => partition.FirstLba)!;
        byte[] volume = CloudInitSeed.Build(
            metaData,
            userData,
            networkConfig,
            BitConverter.ToUInt32(RandomNumberGenerator.GetBytes(4)),
            timeProvider.GetUtcNow().UtcDateTime,
            seed.FirstLba);

        raw.Write(seed.FirstLba * GptLayout.SectorSize, volume);
        raw.Write(layout.BackupEntriesLba * GptLayout.SectorSize, layout.EntryArray());
        raw.Write(layout.BackupLba * GptLayout.SectorSize, layout.BackupHeader());
        raw.Write(layout.EntriesLba * GptLayout.SectorSize, layout.EntryArray());
        raw.Write(GptLayout.SectorSize, layout.PrimaryHeader());
        raw.Flush();

        try
        {
            raw.UpdateProperties();
        }
        catch (DeploymentStepException exception)
        {
            log.Warning($"{exception.Message} The seed is on the disk all the same.");
        }

        log.Information(
            $"The cloud-init seed is partition {seed.Number} of disk {disk.Number}, {ByteSize.Format(CloudInitSeed.SizeBytes)} labelled " +
            $"{CloudInitSeed.Label}, with {(networkConfig is null ? "meta-data and user-data" : "meta-data, user-data and network-config")}.");
        context.Progress.Report(100);

        return Task.FromResult(StepResult.Done(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [RunVariables.SeedPartition] = seed.Number.ToString(System.Globalization.CultureInfo.InvariantCulture),
        }));
    }

    // The seed's files with the machine's values filled in. The run renders them too before its first step, so a value the
    // machine lacks stops the run before the disk is erased.
    public static (string MetaData, string UserData, string? NetworkConfig) Render(
        WriteCloudInitSeedStep step,
        string? computerName,
        MachineVariables machine)
    {
        ArgumentNullException.ThrowIfNull(step);
        ArgumentNullException.ThrowIfNull(machine);

        Dictionary<string, string?> values = new(StringComparer.Ordinal)
        {
            [MachineVariableNames.ComputerName] = computerName,
            [MachineVariableNames.Manufacturer] = machine.Manufacturer,
            [MachineVariableNames.Model] = machine.Model,
            [MachineVariableNames.SerialNumber] = machine.SerialNumber,
            [MachineVariableNames.SmbiosUuid] = machine.SmbiosUuid,
            [MachineVariableNames.MacAddress] = machine.MacAddresses.Count > 0 ? ColonSeparated(machine.MacAddresses[0]) : null,
        };

        try
        {
            return (
                CloudInitTemplate.Render(step.MetaData ?? "", values),
                CloudInitTemplate.Render(step.UserData ?? "", values),
                step.NetworkConfig is null ? null : CloudInitTemplate.Render(step.NetworkConfig, values));
        }
        catch (InvalidOperationException exception)
        {
            string remedy = exception.Message.Contains($"{{{{{MachineVariableNames.ComputerName}}}}}", StringComparison.Ordinal)
                ? "Assign the sequence with a computer name, or leave the placeholder out of the seed."
                : "The machine's firmware does not report it. Leave the placeholder out of the seed.";

            throw new DeploymentStepException($"{exception.Message} {remedy}", exception);
        }
    }

    // As cloud-init and netplan write MAC addresses: lower case, in pairs separated by colons.
    private static string ColonSeparated(string mac)
    {
        string digits = new([.. mac.Where(char.IsAsciiHexDigit)]);

        return digits.Length == 12
            ? string.Join(':', Enumerable.Range(0, 6).Select(pair => digits.Substring(pair * 2, 2))).ToLowerInvariant()
            : mac.ToLowerInvariant();
    }
}
