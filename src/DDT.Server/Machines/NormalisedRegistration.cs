// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Agents;
using DDT.Contracts.Images;
using DDT.Contracts.Machines;

namespace DDT.Server.Machines;

// A member is null where the agent could not tell, sent what cannot be right, or is older than the member: Disks and
// EligibleDiskCount, SecureBootEnabled, TrustedUefiCas, ChassisType, and Facts, which agents older than version 3
// sequences omit.
public sealed record NormalisedRegistration(
    string SmbiosUuid,
    string PrimaryMac,
    IReadOnlyList<string> MacAddresses,
    string? Manufacturer,
    string? Model,
    string? SerialNumber,
    string AgentVersion,
    string? ResumeToken,
    string? Disks,
    int? EligibleDiskCount,
    string? RunToken = null,
    int SequenceVersion = 0,
    AgentEnvironment Environment = AgentEnvironment.WindowsPE,
    bool? SecureBootEnabled = null,
    UefiCa? TrustedUefiCas = null,
    int? ChassisType = null,
    MachineFacts? Facts = null)
{
    // What an older agent does not report, its disks and its facts, stays as a newer one reported it rather than turn
    // unknown.
    public void ApplyTo(Machine machine, string? address, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(machine);

        machine.PrimaryMac = PrimaryMac;
        machine.MacAddresses = string.Join(',', MacAddresses);
        machine.Manufacturer = Manufacturer;
        machine.Model = Model;
        machine.SerialNumber = SerialNumber;
        machine.AgentVersion = AgentVersion;
        machine.SequenceVersion = SequenceVersion;
        machine.AgentEnvironment = Environment;
        machine.SecureBootEnabled = SecureBootEnabled;
        machine.TrustedUefiCas = TrustedUefiCas;
        machine.ChassisType = ChassisType;
        machine.LastSeenUtc = now;
        machine.LastSeenAddress = address;

        if (EligibleDiskCount is not null)
        {
            machine.Disks = Disks;
            machine.EligibleDiskCount = EligibleDiskCount;
        }

        if (Facts is { } facts)
        {
            machine.Facts = MachineFactsDocuments.Write(facts);
        }
    }
}
