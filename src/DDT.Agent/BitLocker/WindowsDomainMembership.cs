// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Agent.Deployment;

namespace DDT.Agent.BitLocker;

// Asks netapi32 how the machine is joined. It sees a join through the answer file and one by Join the domain alike.
public sealed class WindowsDomainMembership : IDomainMembership
{
    public bool IsJoined() => DomainJoinNativeMethods.IsJoinedToDomain();
}
