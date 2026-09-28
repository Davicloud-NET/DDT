// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Contracts.Sequences;

public static class MachineVariableNames
{
    public const string Manufacturer = "Manufacturer";
    public const string Model = "Model";
    public const string SerialNumber = "SerialNumber";
    public const string SmbiosUuid = "SmbiosUuid";

    // Every MAC address the machine reported, so a condition on it holds if any address matches.
    public const string MacAddress = "MacAddress";
    public const string ComputerName = "ComputerName";

    // WindowsPE or Windows: the phase the step would run in.
    public const string Phase = "Phase";

    // Version 3. Laptop, Desktop and so on, as DeviceKind names them.
    public const string DeviceKind = "DeviceKind";

    // The name a person knows the model by: Lenovo's SMBIOS system version, such as "ThinkPad T14 Gen 4", where its
    // Model is a type number, and Model otherwise.
    public const string FriendlyModel = "FriendlyModel";

    public const string MemoryMegabytes = "MemoryMegabytes";
    public const string ProcessorName = "ProcessorName";
    public const string ProcessorCores = "ProcessorCores";
    public const string LogicalProcessors = "LogicalProcessors";
    public const string TpmPresent = "TpmPresent";

    // 2.0 or 1.2.
    public const string TpmVersion = "TpmVersion";
    public const string SecureBootCapable = "SecureBootCapable";
    public const string SecureBootEnabled = "SecureBootEnabled";

    // The primary adapter's.
    public const string IPv4Address = "IPv4Address";
    public const string IPv4PrefixLength = "IPv4PrefixLength";

    // The primary adapter's network, such as 10.0.0.0/24.
    public const string Subnet = "Subnet";
    public const string DefaultGateway = "DefaultGateway";
    public const string DnsSuffix = "DnsSuffix";
    public const string DhcpServer = "DhcpServer";
    public const string PrimaryMacAddress = "PrimaryMacAddress";

    // SMBIOS: the system's version, family and SKU (type 1), its asset tag (type 3), the baseboard's product (type 2),
    // and the BIOS version and date (type 0), the date as yyyy-MM-dd.
    public const string SystemVersion = "SystemVersion";
    public const string SystemFamily = "SystemFamily";
    public const string SystemSku = "SystemSku";
    public const string AssetTag = "AssetTag";
    public const string BaseboardProduct = "BaseboardProduct";
    public const string BiosVersion = "BiosVersion";
    public const string BiosDate = "BiosDate";

    // Run variables: whether the last step that ran failed, and the exit code of the last script, so a repeat can try
    // again until a step works.
    public const string LastStepFailed = "LastStepFailed";
    public const string LastExitCode = "LastExitCode";

    // The variables of versions 1 and 2, frozen: an agent of those versions tests nothing else, and a condition on any
    // other name makes a document version 3.
    public static IReadOnlyList<string> All { get; } =
        [Manufacturer, Model, SerialNumber, SmbiosUuid, MacAddress, ComputerName, Phase];

    // Every name a condition can test besides the sequence's own variables and the values of rules and machine roles,
    // with its type, which decides the operators that fit it. In the order a page lists them.
    public static IReadOnlyDictionary<string, FactType> Catalogue { get; } = new OrderedDictionary<string, FactType>(StringComparer.Ordinal)
    {
        [Manufacturer] = FactType.Text,
        [Model] = FactType.Text,
        [FriendlyModel] = FactType.Text,
        [SerialNumber] = FactType.Text,
        [SmbiosUuid] = FactType.Text,
        [DeviceKind] = FactType.Text,
        [MacAddress] = FactType.Mac,
        [PrimaryMacAddress] = FactType.Mac,
        [ComputerName] = FactType.Text,
        [Phase] = FactType.Text,
        [MemoryMegabytes] = FactType.Number,
        [ProcessorName] = FactType.Text,
        [ProcessorCores] = FactType.Number,
        [LogicalProcessors] = FactType.Number,
        [TpmPresent] = FactType.YesNo,
        [TpmVersion] = FactType.Number,
        [SecureBootCapable] = FactType.YesNo,
        [SecureBootEnabled] = FactType.YesNo,
        [IPv4Address] = FactType.IPv4,
        [IPv4PrefixLength] = FactType.Number,
        [Subnet] = FactType.Text,
        [DefaultGateway] = FactType.IPv4,
        [DnsSuffix] = FactType.Text,
        [DhcpServer] = FactType.IPv4,
        [SystemVersion] = FactType.Text,
        [SystemFamily] = FactType.Text,
        [SystemSku] = FactType.Text,
        [AssetTag] = FactType.Text,
        [BaseboardProduct] = FactType.Text,
        [BiosVersion] = FactType.Text,
        [BiosDate] = FactType.Text,
        [LastStepFailed] = FactType.YesNo,
        [LastExitCode] = FactType.Number,
    };

    // The names whose value changes while a run goes on. Everything else is fixed when the run starts, which is what a
    // share's host may be made of.
    public static IReadOnlyList<string> ChangeDuringRun { get; } = [Phase, LastStepFailed, LastExitCode];
}
