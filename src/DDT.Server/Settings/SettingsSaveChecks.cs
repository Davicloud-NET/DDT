// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Messages;
using DDT.Core.Configuration;
using DDT.Server.Authentication;
using DDT.Server.Data;
using DDT.Server.Ldap;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;

namespace DDT.Server.Settings;

// The checks of a save that need more than the values: the accounts, the framework's own checks of the sign-in scheme,
// and a directory administrator's proof that the new directory values keep them an administrator.
public sealed class SettingsSaveChecks(UserManager<DdtUser> users, IDataProtectionProvider dataProtection, DirectoryProofs proofs)
{
    // What decides whether a directory account still signs in, and with which roles.
    private static readonly string[] s_directoryFields =
        ["Host", "Port", "Transport", "BaseDn", "BindDn", "UserFilter", "ImmutableIdAttribute", "ResolveNestedGroups", "GroupRoleMap"];

    public async Task<SettingsSaveCheck> CheckAsync(
        SettingsSectionDefinition definition,
        SettingsSectionChange change,
        Actor actor,
        SettingsUpdate update,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(change);
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentNullException.ThrowIfNull(update);
        cancellationToken.ThrowIfCancellationRequested();

        SettingsSectionState after = change.After;
        List<SettingProblem> problems = [];
        List<SettingWarning> warnings = [];

        if (definition.Name is SettingsSectionNames.Ldap or SettingsSectionNames.Oidc && !await HasLocalAdministratorAsync().ConfigureAwait(false))
        {
            warnings.Add(new(string.Empty, ServerMessages.SettingsNoLocalAdministrator.With(), SettingWarningCodes.NoLocalAdministrator));
        }

        if (definition.Name == SettingsSectionNames.Oidc && after.Options is OidcOptions { Enabled: true } oidc && !after.Closed)
        {
            problems.AddRange(OidcCandidate.FindProblems(oidc, dataProtection));
        }

        if (definition.Name == SettingsSectionNames.Ldap
            && actor.UserId is { } userId
            && after.Options is LdapOptions { Enabled: true } ldap
            && DirectoryChanged(definition, change.Before, after)
            && await users.FindByIdAsync(userId.ToString("D")).ConfigureAwait(false) is { Source: AccountSource.Directory }
            && !proofs.Accepts(update.DirectoryProof, userId, ldap))
        {
            problems.Add(new(string.Empty, ServerMessages.SettingsLdapTestOwnSignIn.With()));
        }

        return new(problems, warnings);
    }

    private async Task<bool> HasLocalAdministratorAsync()
    {
        IList<DdtUser> administrators = await users.GetUsersInRoleAsync(DdtRoleNames.Administrator).ConfigureAwait(false);

        return administrators.Any(user => user is { Source: AccountSource.Local, IsDisabled: false, PasswordHash: not null });
    }

    private static bool DirectoryChanged(SettingsSectionDefinition definition, SettingsSectionState before, SettingsSectionState after) =>
        s_directoryFields
            .Select(path => definition.FieldOf(path)!)
            .Any(field => !SettingsJson.Same(SettingsJson.Get(before.Values, field), SettingsJson.Get(after.Values, field)))
        || !string.Equals(((LdapOptions)before.Options).BindPassword, ((LdapOptions)after.Options).BindPassword, StringComparison.Ordinal);
}
