// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Settings;

namespace DDT.Contracts.Accounts;

// Creates or saves an account.
public sealed record SaveAccountRequest(
    // The revision the page last read. A new account doesn't have one, so the server ignores it.
    long Revision,
    string Name,
    string UserName,
    string? Domain,
    IReadOnlyList<string> Hosts,
    bool RunAs,
    // Keeps, sets or clears the password, the same way as a setting's secret. A new user name, domain or host needs
    // the password set again. That way a stored password never reaches a destination it wasn't given for.
    SecretUpdate Password)
{
    // A record prints every property by default, and the password must never reach a log.
    public override string ToString() =>
        $"SaveAccountRequest {{ Revision = {Revision}, Name = {Name}, UserName = {UserName}, Domain = {Domain}, RunAs = {RunAs}, "
        + $"Password = {Password?.Action} }}";
}
