// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Agents;
using DDT.Server.Data;

namespace DDT.Server.Deployments;

// Answers to the inputs a run waits for, who gave them, and whether at the machine rather than on the web.
public sealed record AnswersGiven(IReadOnlyList<InputAnswer>? Answers, Actor Actor, bool AtMachine);
