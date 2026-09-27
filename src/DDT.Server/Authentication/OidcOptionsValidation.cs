// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Core.Configuration;

namespace DDT.Server.Authentication;

public static class OidcOptionsValidation
{
    // A role that does not exist would surface only at the first sign in of an unknown identity, as a failure.
    public static IReadOnlyList<SettingProblem> FindProblems(OidcOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        List<SettingProblem> problems = [];
        string? provisioned = DdtRoleNames.Canonical(options.AutoProvisionRole);

        if (provisioned is null)
        {
            problems.Add(new(nameof(OidcOptions.AutoProvisionRole), $"'{options.AutoProvisionRole}' is not a DDT role. Use {DdtRoleNames.Viewer} or {DdtRoleNames.Operator}."));
        }
        else if (provisioned == DdtRoleNames.Administrator)
        {
            problems.Add(new(
                nameof(OidcOptions.AutoProvisionRole),
                $"{DdtRoleNames.Administrator} would make every identity the provider signs in that DDT has not seen an administrator. " +
                $"Use {DdtRoleNames.Viewer} or {DdtRoleNames.Operator}, or map a group to {DdtRoleNames.Administrator} in GroupRoleMap."));
        }

        foreach ((string group, string role) in options.GroupRoleMap)
        {
            if (DdtRoleNames.Canonical(role) is null)
            {
                problems.Add(new($"{nameof(OidcOptions.GroupRoleMap)}:{group}", $"'{role}' is not a DDT role. Use {string.Join(", ", DdtRoleNames.All)}."));
            }
        }

        if (options.GroupRoleMap.Count > 0 && string.IsNullOrWhiteSpace(options.GroupsClaim))
        {
            problems.Add(new(nameof(OidcOptions.GroupsClaim), "GroupRoleMap needs the claim that carries the groups, such as groups."));
        }

        return problems;
    }
}
