// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using Microsoft.AspNetCore.Identity;

namespace DDT.Server.Authentication;

// The credentials were right, but the groups of the account give it no role. It counts as not allowed wherever that is
// not looked for, and the sign-in tells it apart to say why.
public sealed class NoRoleSignInResult : SignInResult
{
    private NoRoleSignInResult() => IsNotAllowed = true;

    public static NoRoleSignInResult Instance { get; } = new();
}
