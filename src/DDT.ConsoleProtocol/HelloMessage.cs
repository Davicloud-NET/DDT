// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.ConsoleProtocol;

// The first message from each side. The agent only accepts its own Version. This message never changes, so two versions
// can always tell each other apart. Program names the sender for the log, such as "DDT agent 1.4.0".
public sealed record HelloMessage(int Version, string Program) : ConsoleMessage
{
    public const int CurrentVersion = 3;
}
