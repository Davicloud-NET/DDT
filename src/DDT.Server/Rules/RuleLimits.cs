// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Server.Rules;

public static class RuleLimits
{
    // Room for every name the upgrade gives an assignment rule: "Model", a manufacturer and a model of
    // HardwareModels.MaxLength each, and the spaces between.
    public const int MaxNameLength = 300;

    public const int MaxDescriptionLength = 1024;

    public const int MaxRoleNameLength = 128;
}
