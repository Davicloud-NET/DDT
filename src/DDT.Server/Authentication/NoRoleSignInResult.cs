// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using Microsoft.AspNetCore.Identity;

namespace DDT.Server.Authentication;

// The credentials were right, but the account's groups don't give it a role. Code that doesn't check for this type sees
// it as not allowed. The sign-in checks for it to say why.
public sealed class NoRoleSignInResult : SignInResult
{
    private NoRoleSignInResult() => IsNotAllowed = true;

    public static NoRoleSignInResult Instance { get; } = new();
}
