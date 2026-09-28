// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Server.Accounts;

// What the checks of a sequence need of a stored account: where it may go, and whether it has a password this server
// can read. Never the password.
public sealed record AccountFacts(string Name, string? Domain, IReadOnlyList<string> Hosts, bool RunAs, bool PasswordSet, bool PasswordReadable);
