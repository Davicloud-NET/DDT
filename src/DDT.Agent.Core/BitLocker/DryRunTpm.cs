// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Agent.BitLocker;

// Takes every machine as having a TPM, so a dry run goes through the BitLocker steps.
public sealed class DryRunTpm : ITpm
{
    public bool IsPresent() => true;
}
