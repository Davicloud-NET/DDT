// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Server.Data;
using DDT.Server.Endpoints;
using DDT.Server.Settings;
using DDT.Server.Users;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;

namespace DDT.Server.Authentication;

// Finishes a sign-in through the OpenID Connect provider. It logs under ExternalLoginEndpoints' category, which the
// logging settings may name.
internal sealed class ExternalSignIn(
    SignInManager<DdtUser> signInManager,
    UserManager<DdtUser> userManager,
    UserActivity activity,
    DdtSettings settings,
    TimeProvider time,
    ILoggerFactory loggerFactory)
{
    private readonly ILogger _logger = loggerFactory.CreateLogger(typeof(ExternalLoginEndpoints));

    public async Task<ExternalSignInOutcome> CompleteAsync(CancellationToken cancellationToken)
    {
        ExternalLoginInfo? info = await signInManager.GetExternalLoginInfoAsync().ConfigureAwait(false);

        if (info is null)
        {
            return ExternalSignInOutcome.Failed;
        }

        OidcOptions oidc = settings.Current.Oidc;
        GroupRoles mapped = GroupRoles.From(SingleSignOnGroups.Read(info.Principal, oidc.GroupsClaim), oidc.GroupRoleMap);
        DdtUser? existing = await userManager.FindByLoginAsync(info.LoginProvider, info.ProviderKey).ConfigureAwait(false);

        // Identity does not know DDT's disabled flag, so this checks it as the password sign-in does.
        if (existing is { IsDisabled: true })
        {
            return ExternalSignInOutcome.NotAllowed;
        }

        // As with the directory, the groups decide the role of an account single sign-on made, at each sign-in and before
        // the cookie is issued. A local account linked to the identity keeps the role an administrator gave it.
        if (mapped.Decides && existing is { Source: AccountSource.External }
            && await ApplyGroupRoleAsync(existing, info.LoginProvider, mapped, cancellationToken).ConfigureAwait(false) is { } refused)
        {
            return refused;
        }

        SignInResult result = await signInManager
            .ExternalLoginSignInAsync(info.LoginProvider, info.ProviderKey, isPersistent: false, bypassTwoFactor: false)
            .ConfigureAwait(false);

        if (result.Succeeded)
        {
            if (existing is not null)
            {
                await activity.SignedInAsync(existing, cancellationToken).ConfigureAwait(false);
            }

            return ExternalSignInOutcome.SignedIn;
        }

        return result switch
        {
            { RequiresTwoFactor: true } => ExternalSignInOutcome.TwoFactor,
            { IsLockedOut: true } => ExternalSignInOutcome.LockedOut,
            { IsNotAllowed: true } => ExternalSignInOutcome.NotAllowed,

            // DDT will not guess which local account an unknown identity belongs to. Matching on the asserted email
            // address would let any issuer that does not verify addresses take over an account by claiming one.
            _ when !oidc.AutoProvision => ExternalSignInOutcome.Unlinked,
            _ => await ProvisionAsync(info, oidc, mapped, cancellationToken).ConfigureAwait(false),
        };
    }

    // An account in none of the groups loses the role it had.
    private async Task<ExternalSignInOutcome?> ApplyGroupRoleAsync(DdtUser existing, string provider, GroupRoles mapped, CancellationToken cancellationToken)
    {
        (IdentityResult applied, bool changed) = await activity.ApplyGroupRoleAsync(existing, mapped.Role).ConfigureAwait(false);

        if (changed)
        {
            await activity.ChangedAsync(existing, cancellationToken).ConfigureAwait(false);
        }

        if (!applied.Succeeded)
        {
            AuthLog.GroupRoleNotSaved(_logger, provider, existing.UserName, Describe(applied));

            return ExternalSignInOutcome.Failed;
        }

        if (mapped.Role is null)
        {
            AuthLog.NoMappedGroup(_logger, provider, existing.UserName);

            return ExternalSignInOutcome.NoRole;
        }

        return null;
    }

    // The groups come before AutoProvisionRole, and no account is made for an identity they give no role.
    private async Task<ExternalSignInOutcome> ProvisionAsync(ExternalLoginInfo info, OidcOptions oidc, GroupRoles mapped, CancellationToken cancellationToken)
    {
        if (mapped.Decides && mapped.Role is null)
        {
            AuthLog.NoMappedGroup(_logger, info.LoginProvider, info.Principal.Identity?.Name ?? info.ProviderKey);

            return ExternalSignInOutcome.NoRole;
        }

        DdtUser user = new()
        {
            UserName = info.Principal.Identity?.Name ?? info.ProviderKey,
            Email = info.Principal.FindFirst(System.Security.Claims.ClaimTypes.Email)?.Value,
            DisplayName = info.Principal.Identity?.Name,
            Source = AccountSource.External,
            CreatedUtc = time.GetUtcNow(),
        };

        IdentityResult provisioned = await ExternalAccounts
            .ProvisionAsync(userManager, user, info, mapped.Decides ? mapped.Role! : oidc.AutoProvisionRole)
            .ConfigureAwait(false);

        if (!provisioned.Succeeded)
        {
            AuthLog.ProvisionFailed(_logger, info.LoginProvider, user.UserName, Describe(provisioned));

            return ExternalSignInOutcome.NotProvisioned;
        }

        await signInManager.SignInAsync(user, isPersistent: false).ConfigureAwait(false);
        await activity.SignedInAsync(user, cancellationToken).ConfigureAwait(false);

        return ExternalSignInOutcome.SignedIn;
    }

    private static string Describe(IdentityResult result) => string.Join("; ", result.Errors.Select(error => error.Description));
}
