// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.ConsoleProtocol;

// A line of the agent's log, as the server gets it. Time is in UTC by the machine's clock, which in Windows PE can be
// hours off, as the text console shows it. StepId is the run's step that was running when it was written.
public sealed record ConsoleLogLine(DateTimeOffset Time, ConsoleLogLevel Level, string Text, Guid? StepId = null);
