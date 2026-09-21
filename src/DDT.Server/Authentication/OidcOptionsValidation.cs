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

        return DdtRoleNames.All.Contains(options.AutoProvisionRole, StringComparer.OrdinalIgnoreCase)
            ? []
            : [new("AutoProvisionRole", $"'{options.AutoProvisionRole}' is not a DDT role. Use {DdtRoleNames.Viewer} or {DdtRoleNames.Operator}.")];
    }
}
