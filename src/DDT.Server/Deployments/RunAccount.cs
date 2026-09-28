// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Server.Deployments;

// An account given for one run, as RunCredentials reads it back.
public sealed record RunAccount(
    string InputName,
    string UserName,
    // Null when it no longer decrypts with this server's key ring.
    string? Password,
    // Domain, Hosts and RunAs are the destination as the input declared it when the account was given.
    string? Domain,
    IReadOnlyList<string> Hosts,
    bool RunAs,
    string? ProvidedByName,
    bool ProvidedAtMachine,
    DateTimeOffset GivenUtc)
{
    // A record prints every property by default, and the password must never reach a log.
    public override string ToString() =>
        $"RunAccount {{ InputName = {InputName}, UserName = {UserName}, Domain = {Domain}, RunAs = {RunAs}, ProvidedByName = {ProvidedByName} }}";
}
