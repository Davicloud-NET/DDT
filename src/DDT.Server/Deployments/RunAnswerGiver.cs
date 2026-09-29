// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Server.Deployments;

// Who answered a run's inputs, where and when, as each RunAnswer records it.
public sealed record RunAnswerGiver(string? AnsweredBy, bool AtMachine, DateTimeOffset AnsweredUtc);
