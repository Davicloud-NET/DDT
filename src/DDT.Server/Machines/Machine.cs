// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Agents;
using DDT.Contracts.Machines;
using DDT.Server.Data;

namespace DDT.Server.Machines;

public sealed class Machine
{
    public Guid Id { get; set; }

    // Reported by the agent and therefore attacker controllable. Used to recognise a machine
    // across reboots, never as proof of identity.
    public required string SmbiosUuid { get; set; }

    public required string PrimaryMac { get; set; }

    // Comma separated, normalised to twelve upper case hex digits each.
    public string MacAddresses { get; set; } = string.Empty;

    public string? Manufacturer { get; set; }

    public string? Model { get; set; }

    public string? SerialNumber { get; set; }

    // Set by an operator, or by the technician signed in at the machine, and checked by ComputerNames, because it
    // reaches the unattend file and a domain join. Never taken from what the agent reports about itself.
    public string? AssignedName { get; set; }

    public string? AgentVersion { get; set; }

    // From the last registration: the highest sequence version its agent runs, and whether it runs in Windows PE or as
    // the service in the installed Windows.
    public int SequenceVersion { get; set; }

    public AgentEnvironment AgentEnvironment { get; set; }

    public MachineState State { get; set; } = MachineState.Pending;

    // Every issued machine token carries the generation it was minted under. Bumping this
    // invalidates all outstanding tokens for the machine without tracking them individually.
    public int TokenGeneration { get; set; }

    public DateTimeOffset FirstSeenUtc { get; set; }

    public DateTimeOffset LastSeenUtc { get; set; }

    public string? FirstSeenAddress { get; set; }

    public string? LastSeenAddress { get; set; }

    public Guid? ApprovedByUserId { get; set; }

    public DdtUser? ApprovedBy { get; set; }

    public DateTimeOffset? ApprovedUtc { get; set; }

    // Set by the first approval and never cleared, unlike ApprovedUtc, so a machine that someone vouched for
    // once is never taken for a stray registration and removed.
    public DateTimeOffset? FirstApprovedUtc { get; set; }

    public Guid? SignedInByUserId { get; set; }

    // A copy of the user name at the time, so the machines list and the live push need no join.
    public string? SignedInUserName { get; set; }

    public DateTimeOffset? SignedInUtc { get; set; }

    // One display line per disk the agent could install on, as reported at registration.
    public string? Disks { get; set; }

    public int? EligibleDiskCount { get; set; }

    // The deployment that is assigned or running. It is checked on save, so an assignment on the web, a pick at the
    // machine and a registration cannot each start one.
    public Guid? ActiveDeploymentId { get; set; }

    // The newest deployment, active or not, so the machines list loads one deployment per machine.
    public Guid? LastDeploymentId { get; set; }
}
