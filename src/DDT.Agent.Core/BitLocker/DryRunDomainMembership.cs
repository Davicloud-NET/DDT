// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Agent.Deployment;

namespace DDT.Agent.BitLocker;

// Takes the machine as not joined, whatever this computer is, so a dry run never touches its policy.
public sealed class DryRunDomainMembership(AgentLog log) : IDomainMembership
{
    public bool IsJoined()
    {
        log.Information("Dry run: the machine is taken as not joined to a domain.");

        return false;
    }
}
