// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Server.Live;

// Watchers of a machine receive its events; administrators and operators each have a group for what only they may read,
// and every user one for what concerns only them, such as their API tokens.
public static class LiveGroups
{
    public const string Administrators = "administrators";

    // Operators that are not administrators: administrators receive the same events through their own group.
    public const string Operators = "operators";

    public static string Machine(Guid machineId) => $"machine:{machineId:D}";

    public static string User(Guid userId) => $"user:{userId:D}";
}
