// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Contracts.Users;

// What a directory sign-in with this user name would give, found with the bind account and without the user's
// password.
public sealed record DirectoryCheck(
    bool Found,
    string? DistinguishedName,
    string? DisplayName,
    // The distinguished names of every group the user is in, nested ones included, as a sign-in reads them.
    IReadOnlyList<string> Groups,
    // The groups the map names, with the role each gives.
    IReadOnlyList<DirectoryGroupMatch> Matches,
    // Null when a sign-in would give none or be refused.
    string? Role,
    // Why, in English; MessageCode and MessageArgs say the same for a client in the person's language.
    string Message,
    string? MessageCode = null,
    IReadOnlyDictionary<string, object>? MessageArgs = null);
