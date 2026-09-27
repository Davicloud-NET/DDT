// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Contracts.Authentication;

// A single sign-on provider the sign-in page offers. The page starts it at /api/auth/external/start.
public sealed record ExternalProvider(string Scheme, string DisplayName);
