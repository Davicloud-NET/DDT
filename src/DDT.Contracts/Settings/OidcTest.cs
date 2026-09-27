// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Contracts.Settings;

public sealed record OidcTestRequest(string Authority);

// Reached: the provider's discovery document was read. RedirectUri is the address to register at the provider.
public sealed record OidcTestResult(bool Reached, string? Issuer, string RedirectUri, string Message);
