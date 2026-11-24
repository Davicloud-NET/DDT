// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Agent.BitLocker;

namespace DDT.Agent.Tests;

// A TPM that is there or not, and a domain the machine is joined to or not.
internal sealed class FakeTpmAndDomain : ITpm, IDomainMembership
{
    public bool Present { get; set; } = true;

    public bool Joined { get; set; }

    public bool IsPresent() => Present;

    public bool IsJoined() => Joined;
}
