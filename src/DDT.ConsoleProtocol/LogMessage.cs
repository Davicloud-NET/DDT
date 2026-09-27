// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.ConsoleProtocol;

// Log lines in the order the agent wrote them. The first ones after the hello are the newest lines written before the
// console connected.
public sealed record LogMessage(IReadOnlyList<ConsoleLogLine> Lines) : ConsoleMessage;
