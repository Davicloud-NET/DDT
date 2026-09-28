// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Settings;

namespace DDT.Contracts.Accounts;

// Creates or saves an account. Revision is the one the page last read; a new account has none to name. Password keeps,
// sets or clears it as for a setting's secret; a new user name or domain, or another host, needs it set again, so a
// stored password never reaches a destination it was not given for.
public sealed record SaveAccountRequest(
    long Revision,
    string Name,
    string UserName,
    string? Domain,
    IReadOnlyList<string> Hosts,
    bool RunAs,
    SecretUpdate Password)
{
    // A record prints every property by default, and the password must never reach a log.
    public override string ToString() =>
        $"SaveAccountRequest {{ Revision = {Revision}, Name = {Name}, UserName = {UserName}, Domain = {Domain}, RunAs = {RunAs}, "
        + $"Password = {Password?.Action} }}";
}
