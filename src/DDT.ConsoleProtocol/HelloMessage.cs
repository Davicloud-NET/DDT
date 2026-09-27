// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.ConsoleProtocol;

// The first message of each side: the console's as soon as it is connected, and the agent's once it accepts the console.
// Version is the protocol version the sender speaks, and the agent accepts only its own. Program names the sender, such
// as "DDT agent 1.4.0", for the log. This message never changes, so two versions can always tell each other apart.
public sealed record HelloMessage(int Version, string Program) : ConsoleMessage
{
    public const int CurrentVersion = 1;
}
