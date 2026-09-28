// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Contracts.Users;

// What a directory sign-in with this user name would result in. It's looked up with the bind account, without the
// user's password.
public sealed record DirectoryCheck(
    bool Found,
    string? DistinguishedName,
    string? DisplayName,
    // The distinguished names of every group the user is in, including nested ones, as a sign-in reads them.
    IReadOnlyList<string> Groups,
    // The groups the map names, with the role each gives.
    IReadOnlyList<DirectoryGroupMatch> Matches,
    // Null when a sign-in would give no role or be refused.
    string? Role,
    // Why, in English. MessageCode and MessageArgs carry the same message, so a client can show it in the person's
    // language.
    string Message,
    string? MessageCode = null,
    IReadOnlyDictionary<string, object>? MessageArgs = null);
