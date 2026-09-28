// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Server.Accounts;

// What the sequence checks need to know about a stored account: where it may be used, and whether it has a password
// this server can read. It never holds the password itself.
public sealed record AccountFacts(string Name, string? Domain, IReadOnlyList<string> Hosts, bool RunAs, bool PasswordSet, bool PasswordReadable);
