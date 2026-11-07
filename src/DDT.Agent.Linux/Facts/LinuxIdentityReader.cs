// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Agent.Facts;

// The machine's identity in Linux. Without a network adapter it has none, and says so in a sentence for the person at
// the machine, where the reader both systems share would end the agent.
public sealed class LinuxIdentityReader(IMachineIdentityReader identity, LinuxAdapters adapters) : IMachineIdentityReader
{
    public MachineIdentity Read()
    {
        if (adapters.Names().Count == 0)
        {
            throw new NoNetworkAdapterException(LinuxAdapters.NoneMessage);
        }

        return identity.Read();
    }
}
