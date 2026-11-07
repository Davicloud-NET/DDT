// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Agent.Tests;

// Gives each identity once, then keeps giving the last, like a machine whose network comes up late.
internal sealed class SequenceIdentityReader(params MachineIdentity[] identities) : IMachineIdentityReader
{
    private int _reads;

    public MachineIdentity Read() => identities[Math.Min(_reads++, identities.Length - 1)];
}
