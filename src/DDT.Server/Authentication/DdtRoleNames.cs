// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Server.Authentication;

public static class DdtRoleNames
{
    public const string Administrator = "Administrator";
    public const string Operator = "Operator";
    public const string Viewer = "Viewer";

    public static IReadOnlyList<string> All { get; } = [Administrator, Operator, Viewer];
}
