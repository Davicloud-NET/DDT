// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Server.Live;

// A connection that watches a machine joins its group and receives the events only watchers need. The connections of
// administrators join one more group for the events only they may receive, and every connection joins its user's group
// for what concerns only that user, such as their API tokens.
public static class LiveGroups
{
    public const string Administrators = "administrators";

    public static string Machine(Guid machineId) => $"machine:{machineId:D}";

    public static string User(Guid userId) => $"user:{userId:D}";
}
