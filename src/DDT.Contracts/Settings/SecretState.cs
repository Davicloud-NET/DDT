// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Contracts.Settings;

// Unreadable means a secret was stored, but this server's key ring can't decrypt it, so it has to be entered again.
public sealed record SecretState(bool IsSet, bool Unreadable, DateTimeOffset? UpdatedUtc);
