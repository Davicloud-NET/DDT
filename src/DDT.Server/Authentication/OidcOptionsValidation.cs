// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Messages;
using DDT.Core.Configuration;

namespace DDT.Server.Authentication;

public static class OidcOptionsValidation
{
    // A role that doesn't exist would only show up as a failure at the first sign-in of an unknown identity. The values
    // the handler needs are only checked while single sign-on is on, so a section that's off can be filled in step by
    // step.
    public static IReadOnlyList<SettingProblem> FindProblems(OidcOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        List<SettingProblem> problems = [];
        AddRoleProblems(options, problems);

        if (options.GroupRoleMap.Count > 0 && string.IsNullOrWhiteSpace(options.GroupsClaim))
        {
            problems.Add(new(nameof(OidcOptions.GroupsClaim), ServerMessages.SettingsOidcGroupsClaimRequired.With()));
        }

        if (!options.Scopes.Contains("openid", StringComparer.Ordinal))
        {
            problems.Add(new(nameof(OidcOptions.Scopes), ServerMessages.SettingsOidcScopesWithoutOpenid.With()));
        }

        if (!options.Enabled)
        {
            return problems;
        }

        if (!Uri.TryCreate(options.Authority, UriKind.Absolute, out Uri? authority) || authority.Scheme != Uri.UriSchemeHttps)
        {
            problems.Add(new(nameof(OidcOptions.Authority), ServerMessages.SettingsOidcAuthorityRequired.With()));
        }

        if (string.IsNullOrWhiteSpace(options.ClientId))
        {
            problems.Add(new(nameof(OidcOptions.ClientId), ServerMessages.SettingsOidcClientIdRequired.With()));
        }

        return problems;
    }

    // AutoProvisionRole must name a role below Administrator, and the group map may only name roles DDT knows.
    private static void AddRoleProblems(OidcOptions options, List<SettingProblem> problems)
    {
        string? provisioned = DdtRoleNames.Canonical(options.AutoProvisionRole);

        if (provisioned is null)
        {
            problems.Add(new(
                nameof(OidcOptions.AutoProvisionRole),
                ServerMessages.SettingsOidcProvisionRoleUnknown.With(
                    "role",
                    options.AutoProvisionRole ?? string.Empty,
                    "viewer",
                    DdtRoleNames.Viewer,
                    "operator",
                    DdtRoleNames.Operator)));
        }
        else if (provisioned == DdtRoleNames.Administrator)
        {
            problems.Add(new(
                nameof(OidcOptions.AutoProvisionRole),
                ServerMessages.SettingsOidcProvisionAdministrator.With(
                    "administrator",
                    DdtRoleNames.Administrator,
                    "viewer",
                    DdtRoleNames.Viewer,
                    "operator",
                    DdtRoleNames.Operator)));
        }

        foreach ((string group, string role) in options.GroupRoleMap)
        {
            if (DdtRoleNames.Canonical(role) is null)
            {
                problems.Add(new(
                    $"{nameof(OidcOptions.GroupRoleMap)}:{group}",
                    ServerMessages.SettingsRoleUnknown.With("role", role, "roles", string.Join(", ", DdtRoleNames.All))));
            }
        }
    }
}
