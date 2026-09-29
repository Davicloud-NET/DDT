// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Agents;
using DDT.Contracts.Sequences;
using DDT.Core.Machines;
using DDT.Core.Sequences;

namespace DDT.Server.Machines;

// What rules test and templates read about a machine. A board maker's placeholder counts as no value (see
// HardwareModels), so no condition or pattern such as PC-{{SerialNumber}} picks one up. The phase is the one its agent
// registered from.
public static class MachineVariableReader
{
    public static MachineVariables Read(Machine machine)
    {
        ArgumentNullException.ThrowIfNull(machine);

        return new MachineVariables(
            Firmware(machine.Manufacturer),
            Firmware(machine.Model),
            Firmware(machine.SerialNumber),
            machine.SmbiosUuid,
            [.. machine.MacAddresses.Split(',', StringSplitOptions.RemoveEmptyEntries)],
            string.IsNullOrWhiteSpace(machine.AssignedName) ? null : machine.AssignedName.Trim(),
            machine.AgentEnvironment == AgentEnvironment.Windows ? SequencePhase.Windows : SequencePhase.WindowsPE)
        {
            Facts = MachineFactsDocuments.Read(machine.Facts),
            DeviceKind = DeviceKinds.Classify(machine.Manufacturer, machine.Model, machine.ChassisType),
            PrimaryMacAddress = machine.PrimaryMac,
            SecureBootEnabled = machine.SecureBootEnabled,
        };
    }

    private static string? Firmware(string? value) => HardwareModels.IsPlaceholder(value) ? null : HardwareModels.Clean(value);
}
