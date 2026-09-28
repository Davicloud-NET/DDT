// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Messages;

namespace DDT.Server.Ldap;

// UserFound and PasswordAccepted are null when no user or no password was given. Text is what the directory answered.
public sealed record LdapTestOutcome(bool Bound, bool? UserFound, bool? PasswordAccepted, IReadOnlyList<string> Groups, ServerMessage Text)
{
    public string Message => Text.Text;
}
