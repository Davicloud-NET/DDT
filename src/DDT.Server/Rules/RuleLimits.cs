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

    // Every change pushes the whole list and counts the machines each rule matches. So the list must stay short enough
    // to send and count in one go.
    public const int MaxRules = 500;

    public const int MaxRoles = 200;

    // As many as a step's condition may have.
    public const int MaxTests = 20;

    public const int MaxConditionDepth = 4;

    public const int MaxRolesPerRule = 32;

    // The values a rule or a machine role sets. A name longer than MaxValueNameLength is a problem. One longer than
    // MaxStoredValueNameLength isn't stored at all.
    public const int MaxValues = 64;

    public const int MaxValueNameLength = 64;

    public const int MaxStoredValueNameLength = 128;

    public const int MaxValueLength = 1024;
}
