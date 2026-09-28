// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Messages;

namespace DDT.Server.Deployments;

// Field is the answer's field the problem is about: userName or password.
public sealed record RunCredentialProblem(string Field, ServerMessage Message);
