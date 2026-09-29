// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Agents;
using DDT.Contracts.Machines;
using DDT.Contracts.Sequences;
using DDT.Core.Machines;
using DDT.Core.Sequences;

namespace DDT.Agent.Sequences;

// What a run's conditions and templates read of the machine: the identity and facts it registered with, and the run's
// values.
public static class RunMachine
{
    public static MachineVariables Of(MachineIdentity identity, AgentRun run, IReadOnlyDictionary<string, string>? values = null)
    {
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(run);

        return new MachineVariables(
            identity.Manufacturer,
            identity.Model,
            identity.SerialNumber,
            identity.SmbiosUuid,
            identity.MacAddresses,
            run.ComputerName,
            SequencePhase.WindowsPE)
        {
            Facts = identity.Facts is { } facts ? WithoutPlaceholders(facts) : null,
            DeviceKind = DeviceKinds.Classify(identity.Manufacturer, identity.Model, identity.ChassisType),
            PrimaryMacAddress = identity.PrimaryMac,
            SecureBootEnabled = identity.SecureBootEnabled,
            Variables = values ?? run.Values,
        };
    }

    // A board maker's placeholder, such as "Default string", says nothing about the machine. The server drops it at
    // registration too, so a condition tests the same values on both.
    private static MachineFacts WithoutPlaceholders(MachineFacts facts) => facts with
    {
        ProcessorName = Known(facts.ProcessorName),
        SystemVersion = Known(facts.SystemVersion),
        SystemFamily = Known(facts.SystemFamily),
        SystemSku = Known(facts.SystemSku),
        AssetTag = Known(facts.AssetTag),
        BaseboardProduct = Known(facts.BaseboardProduct),
        BiosVersion = Known(facts.BiosVersion),
    };

    private static string? Known(string? value) => HardwareModels.IsPlaceholder(value) ? null : value;
}
