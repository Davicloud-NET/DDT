// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Contracts.Users;

// What a directory sign-in with this user name would give, found with the bind account and without the user's
// password. Groups are the distinguished names of every group the user is in, nested ones included, as a sign-in reads
// them. Matches are those the map names, with the role each gives. Role is what a sign-in would give, null when it
// would give none or be refused, and Message says why in a sentence.
public sealed record DirectoryCheck(
    bool Found,
    string? DistinguishedName,
    string? DisplayName,
    IReadOnlyList<string> Groups,
    IReadOnlyList<DirectoryGroupMatch> Matches,
    string? Role,
    string Message);
