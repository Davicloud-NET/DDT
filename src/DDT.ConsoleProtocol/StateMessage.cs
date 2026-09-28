// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.ConsoleProtocol;

// The whole state, which replaces the one before. The agent sends it once the console is accepted and after every
// change. While one is on its way, only the newest of the changes that follow is sent.
public sealed record StateMessage(ConsoleState State) : ConsoleMessage;
