// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Agent.BitLocker;

// The policy values the AD backup of an OS volume's recovery password needs. The step sets only the absent ones for
// the backup, and removes them after it.
public static class ActiveDirectoryBackupPolicy
{
    public const string KeyPath = @"SOFTWARE\Policies\Microsoft\FVE";

    public static IReadOnlyDictionary<string, int> Values { get; } = new Dictionary<string, int>(StringComparer.Ordinal)
    {
        ["OSRecovery"] = 1,
        ["OSActiveDirectoryBackup"] = 1,
        ["OSActiveDirectoryInfoToStore"] = 1,
        ["ActiveDirectoryBackup"] = 1,
        ["ActiveDirectoryInfoToStore"] = 1,
    };
}
