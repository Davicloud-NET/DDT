// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.ConsoleProtocol;

// The agent's answer to a console that speaks another version, before it closes the pipe and carries on with its text
// console. Version is the agent's. Like HelloMessage, it never changes.
public sealed record RefusedMessage(int Version, string Reason) : ConsoleMessage;
