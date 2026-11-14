// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Agent.BitLocker;

// The machine's BitLocker policy values under ActiveDirectoryBackupPolicy.KeyPath, all DWORDs.
public interface IBitLockerPolicy
{
    // The names the key does not hold yet.
    IReadOnlyList<string> Absent(IEnumerable<string> names);

    void Set(string name, int value);

    void Remove(IReadOnlyList<string> names);
}
