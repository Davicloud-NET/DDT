// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Contracts.Tokens;

// The only answer that carries the secret: the server keeps just its hash, so it cannot be shown again.
public sealed record CreatedApiToken(ApiTokenView Token, string Secret);
