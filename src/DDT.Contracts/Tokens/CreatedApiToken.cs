// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Contracts.Tokens;

// The only response that carries the secret. The server keeps only its hash, so it can't be shown again.
public sealed record CreatedApiToken(ApiTokenView Token, string Secret);
