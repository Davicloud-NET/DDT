// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Agent;

// The system shows no network adapter, so the machine has no identity to register with yet. The message is a sentence
// for the person at the machine, and the registration waits for an adapter instead of ending the agent.
public sealed class NoNetworkAdapterException : Exception
{
    public NoNetworkAdapterException()
        : base("The system shows no network adapter.")
    {
    }

    public NoNetworkAdapterException(string message)
        : base(message)
    {
    }

    public NoNetworkAdapterException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
