// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Messages;

namespace DDT.Contracts.Settings;

// Reached: the provider's discovery document was read. RedirectUri is the address to register at the provider. Message
// is English; Text is the same sentence as a code with its values, for a client in the person's language.
public sealed record OidcTestResult(bool Reached, string? Issuer, string RedirectUri, string Message, ServerMessage? Text = null);
