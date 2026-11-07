// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Agent.Tests;

// A machine whose network adapter the system shows only from the given read on, like one in a dock.
internal sealed class LateAdapterIdentityReader(int readsWithout, string message) : IMachineIdentityReader
{
    public int Reads { get; private set; }

    public MachineIdentity Read() =>
        ++Reads <= readsWithout ? throw new NoNetworkAdapterException(message) : new DryRunMachineIdentityReader(1).Read();
}
