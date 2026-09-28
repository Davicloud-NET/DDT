// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Agent.Deployment;
using DDT.Contracts.Machines;

namespace DDT.Agent.Facts;

// Reads what conditions and rules test besides the machine's identity. A fact that cannot be read stays null and the
// others are still reported, as no fact is worth a machine that cannot register.
public sealed class MachineFactsReader(IFirmwareTables firmware, ISystemHardware hardware, IUefiVariables uefi)
{
    private const ulong KilobytesPerMegabyte = 1024;
    private const ulong BytesPerMegabyte = 1024 * 1024;

    // smbios is the table the identity came from, and network the primary adapter's settings, both null when they could
    // not be read.
    public MachineFacts Read(SmbiosSystemInformation? smbios, NetworkFacts? network)
    {
        ProcessorCount? processors = Try(() => hardware.ProcessorCores() is { } records ? ProcessorCount.From(records) : null);
        byte[]? acpiTables = Try(() => firmware.List(FirmwareTables.AcpiProvider));
        string? tpmVersion = acpiTables is null ? null : AcpiTpm.Version(acpiTables);

        return new MachineFacts
        {
            MemoryMegabytes = Try(ReadMemoryMegabytes),
            ProcessorName = Text(smbios?.ProcessorVersion),
            ProcessorCores = processors?.Cores,
            LogicalProcessors = processors?.LogicalProcessors,
            TpmPresent = acpiTables is null ? null : tpmVersion is not null,
            TpmVersion = tpmVersion,
            SecureBootCapable = Try(ReadSecureBootCapable),
            IPv4Address = network?.IPv4Address,
            IPv4PrefixLength = network?.PrefixLength,
            DefaultGateway = network?.DefaultGateway,
            DnsSuffix = Text(network?.DnsSuffix),
            DhcpServer = network?.DhcpServer,
            SystemVersion = Text(smbios?.Version),
            SystemFamily = Text(smbios?.Family),
            SystemSku = Text(smbios?.Sku),
            AssetTag = Text(smbios?.AssetTag),
            BaseboardProduct = Text(smbios?.BaseboardProduct),
            BiosVersion = Text(smbios?.BiosVersion),
            BiosDate = smbios?.BiosDate,
        };
    }

    // What the SMBIOS memory devices add up to, which is what the machine's label says; what Windows can use where they
    // add up to nothing, as on some virtual machines.
    private long? ReadMemoryMegabytes()
    {
        ulong? installed = Try(hardware.InstalledMemoryKilobytes);

        if (installed is > 0)
        {
            return (long)(installed.Value / KilobytesPerMegabyte);
        }

        ulong? usable = hardware.UsableMemoryBytes();

        return usable is > 0 ? (long)(usable.Value / BytesPerMegabyte) : null;
    }

    // The firmware defines the SecureBoot variable when it can do Secure Boot, on or off. Its variables cannot be read on a
    // machine that did not start in UEFI mode, which leaves the answer unknown, as SecureBootEnabled is there.
    private bool? ReadSecureBootCapable() => uefi.Read("SecureBoot") is not null;

    // Printable, a control character as a space, and no longer than the server keeps.
    private static string? Text(string? value)
    {
        if (value is null)
        {
            return null;
        }

        string printable = new([.. value.Select(character => char.IsControl(character) ? ' ' : character)]);
        string trimmed = printable.Trim();

        if (trimmed.Length == 0)
        {
            return null;
        }

        return trimmed.Length <= AgentLimits.MaxFactLength ? trimmed : trimmed[..AgentLimits.MaxFactLength].TrimEnd();
    }

    private static T? Try<T>(Func<T?> read)
    {
        try
        {
            return read();
        }
        catch (Exception)
        {
            return default;
        }
    }
}
