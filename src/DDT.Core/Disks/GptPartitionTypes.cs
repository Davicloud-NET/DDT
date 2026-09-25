// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Core.Disks;

public static class GptPartitionTypes
{
    public static readonly Guid EfiSystem = new("c12a7328-f81f-11d2-ba4b-00a0c93ec93b");

    // Microsoft basic data, which Linux mounts as well.
    public static readonly Guid BasicData = new("ebd0a0a2-b9e5-4433-87c0-68b6b72699c7");

    public static readonly Guid LinuxFileSystem = new("0fc63daf-8483-4772-8e79-3d69d8477de4");
}
