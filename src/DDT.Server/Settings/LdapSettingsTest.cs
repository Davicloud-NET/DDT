// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Security.Claims;
using System.Text.Json.Nodes;
using DDT.Contracts.Messages;
using DDT.Contracts.Settings;
using DDT.Server.Authentication;
using DDT.Server.Data;
using DDT.Server.Endpoints;
using DDT.Server.Ldap;
using Microsoft.AspNetCore.Identity;

namespace DDT.Server.Settings;

// Tries the LDAP values as the form holds them, before they're saved. The user check is subject to the same lockout as
// a directory sign-in, but it signs nobody in.
internal sealed class LdapSettingsTest(DdtSettings settings, ILdapTester tester, UserManager<DdtUser> users, DirectoryProofs proofs)
{
    public const string BindPasswordField = "bindPassword";

    // Returns null when the values end up with no bind password. That happens when they name a different server than
    // the stored password's and don't bring a password of their own.
    public async Task<LdapTestResult?> TestAsync(LdapTestRequest request, ClaimsPrincipal principal, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(principal);

        if (Candidate(request) is not { } candidate)
        {
            return null;
        }

        string? userName = string.IsNullOrWhiteSpace(request.UserName) ? null : request.UserName.Trim();
        string? userPassword = string.IsNullOrEmpty(request.Password) ? null : request.Password;
        DdtUser? account = userName is null ? null : await users.FindByNameAsync(userName).ConfigureAwait(false);

        if (account is not null && userName is not null && userPassword is not null
            && await RefusalAsync(account, userName).ConfigureAwait(false) is { } refusal)
        {
            LdapTestOutcome bound = await tester.TestAsync(candidate, null, null, cancellationToken).ConfigureAwait(false);
            ServerMessage said = bound.Bound ? refusal : bound.Text;

            return new LdapTestResult(bound.Bound, null, null, [], null, said.Text, null, said);
        }

        LdapTestOutcome outcome = await tester.TestAsync(candidate, userName, userPassword, cancellationToken).ConfigureAwait(false);

        if (account is not null && outcome.PasswordAccepted == false)
        {
            await users.AccessFailedAsync(account).ConfigureAwait(false);
        }

        GroupRoles mapped = GroupRoles.From(outcome.Groups, candidate.GroupRoleMap);
        string? role = mapped.Decides ? mapped.Role : null;
        ServerMessage message = (mapped.Decides && outcome.UserFound == true, role) switch
        {
            (false, _) => outcome.Text,
            (true, null) => ServerMessages.SettingsLdapTestNoRole.With("result", outcome.Text),
            (true, { } given) => ServerMessages.SettingsLdapTestRole.With("result", outcome.Text, "role", DirectorySignInService.RoleName(given)),
        };

        // Proof that the administrator testing these values stays an administrator with them. A directory account
        // needs it to save them.
        string? proof = Principals.UserId(principal) is { } callerId && KeepsAdministrator(principal, userName, outcome, mapped)
            ? proofs.Issue(callerId, candidate)
            : null;

        return new LdapTestResult(outcome.Bound, outcome.UserFound, outcome.PasswordAccepted, outcome.Groups, role, message.Text, proof, message);
    }

    // Fields locked by configuration take their configured values. A stored bind password only goes to the server it
    // was entered for.
    private LdapOptions? Candidate(LdapTestRequest request)
    {
        SettingsSectionState state = settings.Current[SettingsSectionNames.Ldap];
        LdapSettingsSection definition = SettingsDefinitions.Ldap;
        JsonObject values = SettingsApi.Ldap.Document(request.Values);

        foreach (SettingLockState locked in state.Locks.Where(locked => !locked.Field.IsSecret))
        {
            SettingsJson.Set(values, locked.Field, SettingsJson.Get(state.Values, locked.Field));
        }

        SettingField bindPassword = definition.Field(BindPasswordField)!;
        LdapOptions current = (LdapOptions)state.Options;
        SecretUpdate change = request.Secrets?.GetValueOrDefault(bindPassword.Name) ?? new SecretUpdate(SecretAction.Keep, null);
        bool sameServer = LdapSettingsSection.Destination
            .Select(path => definition.FieldOf(path)!)
            .All(field => SettingsJson.Same(SettingsJson.Get(values, field), SettingsJson.Get(state.Values, field)));

        string? password = state.IsLocked(bindPassword)
            ? current.BindPassword
            : change.Action switch
            {
                SecretAction.Set => change.Value,
                SecretAction.Clear => string.Empty,
                _ when sameServer || string.IsNullOrEmpty(current.BindPassword) => current.BindPassword,
                _ => null,
            };

        return password is null ? null : definition.ReadOptions(values, new Dictionary<string, string?> { [bindPassword.Name] = password });
    }

    private async Task<ServerMessage?> RefusalAsync(DdtUser account, string userName) => account switch
    {
        { Source: not AccountSource.Directory } => ServerMessages.SettingsLdapTestNotDirectoryAccount.With("name", userName),
        { IsDisabled: true } => ServerMessages.SettingsLdapTestAccountDisabled.With("name", userName),
        _ when await users.IsLockedOutAsync(account).ConfigureAwait(false) => ServerMessages.SettingsLdapTestLockedOut.With("name", userName),
        _ => null,
    };

    private static bool KeepsAdministrator(ClaimsPrincipal principal, string? userName, LdapTestOutcome outcome, GroupRoles mapped) =>
        outcome.PasswordAccepted == true
        && string.Equals(principal.Identity?.Name, userName, StringComparison.OrdinalIgnoreCase)
        && ((mapped.Decides && mapped.Role == DdtRoleNames.Administrator) || (!mapped.Decides && principal.IsInRole(DdtRoleNames.Administrator)));
}
