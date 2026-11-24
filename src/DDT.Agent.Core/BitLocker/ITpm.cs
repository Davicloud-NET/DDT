// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Agent.BitLocker;

// Whether the machine has a TPM Windows can use. False too where the TPM Base Services are missing, as in a boot
// image without WinPE-SecureStartup.
public interface ITpm
{
    bool IsPresent();
}
