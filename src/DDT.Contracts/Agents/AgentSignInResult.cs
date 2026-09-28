// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Contracts.Agents;

// Every result of checking the credentials is a 200 with a status. A 401 always means the machine token was refused.
public sealed record AgentSignInResult(AgentSignInStatus Status);
