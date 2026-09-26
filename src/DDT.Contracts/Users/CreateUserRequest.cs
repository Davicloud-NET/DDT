// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Contracts.Users;

// A local account. Role is Administrator, Operator or Viewer.
public sealed record CreateUserRequest(string UserName, string DisplayName, string? Email, string Role);
