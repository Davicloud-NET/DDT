// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Sequences;

namespace DDT.Core.Sequences;

// What step conditions test, named by MachineVariableNames. MacAddresses holds every address the machine reported.
public sealed record MachineVariables(
    string? Manufacturer,
    string? Model,
    string? SerialNumber,
    string SmbiosUuid,
    IReadOnlyList<string> MacAddresses,
    string? ComputerName,
    SequencePhase Phase)
{
    // Empty for an unknown variable and for a value the machine did not report.
    public IReadOnlyList<string> Values(string variable) => variable switch
    {
        MachineVariableNames.Manufacturer => One(Manufacturer),
        MachineVariableNames.Model => One(Model),
        MachineVariableNames.SerialNumber => One(SerialNumber),
        MachineVariableNames.SmbiosUuid => One(SmbiosUuid),
        MachineVariableNames.MacAddress => MacAddresses,
        MachineVariableNames.ComputerName => One(ComputerName),
        MachineVariableNames.Phase => [Phase == SequencePhase.Windows ? nameof(SequencePhase.Windows) : nameof(SequencePhase.WindowsPE)],
        _ => [],
    };

    private static IReadOnlyList<string> One(string? value) => value is null ? [] : [value];
}
