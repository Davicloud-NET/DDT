// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Server.Data;
using Microsoft.AspNetCore.Identity;

namespace DDT.Server.Authentication;

public static class ExternalAccounts
{
    // All of it or nothing. Without its link the account would be created again at the next sign in, and without its
    // role it signs in to nothing. The link also fails when the identity already belongs to an account whose sign in
    // stopped short, for example at its second factor: signing in to a new account instead would skip that factor.
    public static async Task<IdentityResult> ProvisionAsync(UserManager<DdtUser> users, DdtUser user, ExternalLoginInfo info, string role)
    {
        ArgumentNullException.ThrowIfNull(users);
        ArgumentNullException.ThrowIfNull(user);
        ArgumentNullException.ThrowIfNull(info);

        IdentityResult result = await users.CreateAsync(user).ConfigureAwait(false);

        if (!result.Succeeded)
        {
            return result;
        }

        result = await users.AddLoginAsync(user, info).ConfigureAwait(false);

        if (result.Succeeded)
        {
            result = await users.AddToRoleAsync(user, role).ConfigureAwait(false);
        }

        if (!result.Succeeded)
        {
            IdentityResult deleted = await users.DeleteAsync(user).ConfigureAwait(false);

            if (!deleted.Succeeded)
            {
                return IdentityResult.Failed([.. result.Errors, .. deleted.Errors]);
            }
        }

        return result;
    }
}
