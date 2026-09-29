// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Messages;

namespace DDT.Server.Deployments;

// Name is the input the problem is about, Field the request's field, such as answers.Owner.
public sealed record AnswerProblem(string Name, string Field, ServerMessage Message);
