// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Contracts.Tokens;

// Role is Administrator, Operator or Viewer, and no higher than the creator's own. Without ExpiresInDays the token lasts
// 90 days.
public sealed record CreateApiTokenRequest(string Name, string Role, int? ExpiresInDays = null);
