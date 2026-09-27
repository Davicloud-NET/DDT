// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.ConsoleProtocol;

// What went wrong, in the words the agent logs, and what can be done about it.
public sealed record ConsoleProblem(string Reason, ConsoleRemedy Remedy);
