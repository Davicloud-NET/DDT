// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Server.Accounts;

// An account's fields as a save request gives them, checked and trimmed.
internal sealed record AccountFields(string Name, string UserName, string? Domain, IReadOnlyList<string> Hosts, bool RunAs);
