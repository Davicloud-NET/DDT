// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.ConsoleProtocol;

// A line of the agent's log, as the server gets it. Time is UTC by the machine's clock, the same time the text console
// shows. In Windows PE that clock can be hours off. StepId is the step that was running when the line was written.
public sealed record ConsoleLogLine(DateTimeOffset Time, ConsoleLogLevel Level, string Text, Guid? StepId = null);
