// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Agents;

namespace DDT.Contracts.Deployments;

// Answers given on the machine's page to a run that waits at its start for its inputs.
public sealed record AnswerInputsRequest(IReadOnlyList<InputAnswer> Answers);
