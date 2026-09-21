// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Contracts.Agents;

// Typed by the technician at the machine. TwoFactorCode is sent once the server has asked for it.
public sealed record AgentSignInRequest(string UserName, string Password, string? TwoFactorCode);
