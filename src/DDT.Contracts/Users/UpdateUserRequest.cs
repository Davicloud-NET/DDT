// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Contracts.Users;

// A field left out, or null, stays as it is. An empty display name or email address clears it.
public sealed record UpdateUserRequest(string? DisplayName, string? Email, string? Role);
