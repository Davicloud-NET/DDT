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

    public static IReadOnlyList<string> All { get; } =
        [Manufacturer, Model, SerialNumber, SmbiosUuid, MacAddress, ComputerName, Phase];
}
