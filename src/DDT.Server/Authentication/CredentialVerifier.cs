// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Server.Data;
using DDT.Server.Ldap;
using Microsoft.AspNetCore.Identity;

namespace DDT.Server.Authentication;

// Checks credentials against the same accounts, lockout and directory as the web sign-in, but doesn't sign anyone in.
// The person typing them sits at a machine and isn't the client of this request.
public sealed class CredentialVerifier(
    UserManager<DdtUser> userManager,
    SignInManager<DdtUser> signInManager,
    DirectorySignInService directory)
{
    public async Task<(SignInResult Result, DdtUser? User)> VerifyAsync(
        string userName,
        string password,
        string? twoFactorCode,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userName);
        ArgumentNullException.ThrowIfNull(password);

        DdtUser? user = await userManager.FindByNameAsync(userName).ConfigureAwait(false);

        if (user?.Source == AccountSource.Directory || (user is null && directory.Enabled))
        {
            return await directory.AuthenticateAsync(userName, password, cancellationToken).ConfigureAwait(false);
        }

        // An unknown user and a wrong password must look the same, both in the response and in timing. A single sign-on
        // account has no password to check, and a guess must not lock it out.
        if (user is null or { Source: AccountSource.External })
        {
            _ = userManager.PasswordHasher.HashPassword(new DdtUser { UserName = userName }, password);

            return (SignInResult.Failed, null);
        }

        if (user.IsDisabled)
        {
            return (SignInResult.NotAllowed, null);
        }

        SignInResult result = await signInManager
            .CheckPasswordSignInAsync(user, password, lockoutOnFailure: true)
            .ConfigureAwait(false);

        if (!result.Succeeded)
        {
            return (result, null);
        }

        if (!user.TwoFactorEnabled)
        {
            return (result, user);
        }

        return await SecondFactorAsync(user, twoFactorCode).ConfigureAwait(false);
    }

    private async Task<(SignInResult Result, DdtUser? User)> SecondFactorAsync(DdtUser user, string? twoFactorCode)
    {
        if (string.IsNullOrWhiteSpace(twoFactorCode))
        {
            return (SignInResult.TwoFactorRequired, null);
        }

        bool valid = await userManager.VerifyTwoFactorTokenAsync(
            user,
            userManager.Options.Tokens.AuthenticatorTokenProvider,
            twoFactorCode.Replace(" ", string.Empty, StringComparison.Ordinal)).ConfigureAwait(false);

        if (!valid)
        {
            await userManager.AccessFailedAsync(user).ConfigureAwait(false);

            return (await userManager.IsLockedOutAsync(user).ConfigureAwait(false) ? SignInResult.LockedOut : SignInResult.Failed, null);
        }

        // With two factor on, Identity does not reset the failure count for a correct password alone. The web
        // sign in resets it after the code, and so must this, or typos at machines add up to a lockout.
        IdentityResult reset = await userManager.ResetAccessFailedCountAsync(user).ConfigureAwait(false);

        return reset.Succeeded ? (SignInResult.Success, user) : (SignInResult.Failed, null);
    }
}
