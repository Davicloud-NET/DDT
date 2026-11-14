// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Server.Records;

public static class RecoveryKeyIds
{
    // As manage-bde and BitLocker's recovery screen write a key ID.
    public static string Shown(Guid protectorId) => protectorId.ToString("D").ToUpperInvariant();
}
