// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Globalization;
using DDT.Contracts.Machines;
using DDT.Contracts.Sequences;
using DDT.Core.Machines;

namespace DDT.Core.Sequences;

// What conditions test and templates read, named by MachineVariableNames and looked up ignoring case. MacAddresses
// holds every address the machine reported.
public sealed record MachineVariables(
    string? Manufacturer,
    string? Model,
    string? SerialNumber,
    string SmbiosUuid,
    IReadOnlyList<string> MacAddresses,
    string? ComputerName,
    SequencePhase Phase)
{
    private const string Lenovo = "LENOVO";

    public MachineFacts? Facts { get; init; }

    public DeviceKind? DeviceKind { get; init; }

    // Null takes the first of MacAddresses, which the agent reports first.
    public string? PrimaryMacAddress { get; init; }

    public bool? SecureBootEnabled { get; init; }

    // The run's values and variables by name: what inputs, rules, roles, defaults and steps set, with LastStepFailed
    // and LastExitCode.
    public IReadOnlyDictionary<string, string>? Variables { get; init; }

    // The name a person knows the model by: Lenovo's SMBIOS system version, where Model is a type number such as 21HD,
    // and Model otherwise.
    public string? FriendlyModel =>
        HardwareModels.Normalize(Manufacturer) == Lenovo
        && HardwareModels.Clean(Facts?.SystemVersion) is { } version
        && !HardwareModels.IsPlaceholder(version)
            ? version
            : Model;

    // The primary adapter's network, such as 10.0.0.0/24.
    public string? Subnet =>
        Facts is { IPv4Address: { } address, IPv4PrefixLength: { } prefix }
        && Ipv4.TryParse(address, out uint parsed)
        && prefix is >= 0 and <= 32
            ? string.Create(CultureInfo.InvariantCulture, $"{Ipv4.Format(parsed & Ipv4.Mask(prefix))}/{prefix}")
            : null;

    // Empty for an unknown name and for a value the machine did not report. A fact is always the machine's, whatever
    // Variables hold, except ComputerName: a run's value or a step may set it, and the latest wins.
    public IReadOnlyList<string> Values(string variable)
    {
        ArgumentNullException.ThrowIfNull(variable);

        return Fact(variable) switch
        {
            MachineVariableNames.Manufacturer => One(Manufacturer),
            MachineVariableNames.Model => One(Model),
            MachineVariableNames.SerialNumber => One(SerialNumber),
            MachineVariableNames.SmbiosUuid => One(SmbiosUuid),
            MachineVariableNames.MacAddress => MacAddresses,
            MachineVariableNames.ComputerName => One(Variable(MachineVariableNames.ComputerName) ?? ComputerName),
            MachineVariableNames.Phase => [PhaseName],
            MachineVariableNames.DeviceKind => One(DeviceKind?.ToString()),
            MachineVariableNames.FriendlyModel => One(FriendlyModel),
            MachineVariableNames.MemoryMegabytes => One(Number(Facts?.MemoryMegabytes)),
            MachineVariableNames.ProcessorName => One(Facts?.ProcessorName),
            MachineVariableNames.ProcessorCores => One(Number(Facts?.ProcessorCores)),
            MachineVariableNames.LogicalProcessors => One(Number(Facts?.LogicalProcessors)),
            MachineVariableNames.TpmPresent => One(YesNo(Facts?.TpmPresent)),
            MachineVariableNames.TpmVersion => One(Facts?.TpmVersion),
            MachineVariableNames.SecureBootCapable => One(YesNo(Facts?.SecureBootCapable)),
            MachineVariableNames.SecureBootEnabled => One(YesNo(SecureBootEnabled)),
            MachineVariableNames.IPv4Address => One(Facts?.IPv4Address),
            MachineVariableNames.IPv4PrefixLength => One(Number(Facts?.IPv4PrefixLength)),
            MachineVariableNames.Subnet => One(Subnet),
            MachineVariableNames.DefaultGateway => One(Facts?.DefaultGateway),
            MachineVariableNames.DnsSuffix => One(Facts?.DnsSuffix),
            MachineVariableNames.DhcpServer => One(Facts?.DhcpServer),
            MachineVariableNames.PrimaryMacAddress => One(PrimaryMacAddress ?? (MacAddresses.Count > 0 ? MacAddresses[0] : null)),
            MachineVariableNames.SystemVersion => One(Facts?.SystemVersion),
            MachineVariableNames.SystemFamily => One(Facts?.SystemFamily),
            MachineVariableNames.SystemSku => One(Facts?.SystemSku),
            MachineVariableNames.AssetTag => One(Facts?.AssetTag),
            MachineVariableNames.BaseboardProduct => One(Facts?.BaseboardProduct),
            MachineVariableNames.BiosVersion => One(Facts?.BiosVersion),
            MachineVariableNames.BiosDate => One(Facts?.BiosDate),
            _ => One(Variable(variable)),
        };
    }

    // The one value a template puts in: the first of several, such as the MAC address the machine reported first.
    public string? Value(string variable) => Values(variable) is [var first, ..] ? first : null;

    // The catalogue's spelling of a fact's name, or null for any other name. LastStepFailed and LastExitCode are in the
    // catalogue, but the run sets them, so they are read from Variables.
    public static string? Fact(string variable)
    {
        ArgumentNullException.ThrowIfNull(variable);

        foreach (string name in MachineVariableNames.Catalogue.Keys)
        {
            if (string.Equals(name, variable, StringComparison.OrdinalIgnoreCase))
            {
                return name is MachineVariableNames.LastStepFailed or MachineVariableNames.LastExitCode ? null : name;
            }
        }

        return null;
    }

    private string? Variable(string name)
    {
        if (Variables is null)
        {
            return null;
        }

        if (Variables.TryGetValue(name, out string? value))
        {
            return value;
        }

        foreach ((string key, string found) in Variables)
        {
            if (string.Equals(key, name, StringComparison.OrdinalIgnoreCase))
            {
                return found;
            }
        }

        return null;
    }

    private string PhaseName => Phase == SequencePhase.Windows ? nameof(SequencePhase.Windows) : nameof(SequencePhase.WindowsPE);

    private static IReadOnlyList<string> One(string? value) => value is null ? [] : [value];

    private static string? Number(long? value) => value?.ToString(CultureInfo.InvariantCulture);

    private static string? YesNo(bool? value) => value switch
    {
        true => "true",
        false => "false",
        null => null,
    };
}
