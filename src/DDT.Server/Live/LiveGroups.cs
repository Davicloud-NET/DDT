// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Server.Live;

// A connection that watches a machine joins its group and receives the events only watchers need.
public static class LiveGroups
{
    public static string Machine(Guid machineId) => $"machine:{machineId:D}";
}
