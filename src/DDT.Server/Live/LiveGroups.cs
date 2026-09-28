// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Server.Live;

// Connections that watch a machine get its events. Administrators and operators each have a group for what only they
// may read. Every user has one for what only concerns them, such as their API tokens.
public static class LiveGroups
{
    public const string Administrators = "administrators";

    // Operators who aren't administrators. Administrators get the same events through their own group.
    public const string Operators = "operators";

    public static string Machine(Guid machineId) => $"machine:{machineId:D}";

    public static string User(Guid userId) => $"user:{userId:D}";
}
