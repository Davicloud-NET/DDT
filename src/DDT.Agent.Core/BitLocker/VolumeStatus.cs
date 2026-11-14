// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Agent.BitLocker;

// Win32_EncryptableVolume's numbers: ConversionStatus (0 decrypted, 1 encrypted, 2 encrypting, ...), the percent
// encrypted, and whether protection is on.
public sealed record VolumeStatus(int Conversion, int Percent, bool Protected)
{
    // Protection turns on by itself once encryption reaches 100 %.
    public bool IsDone => Protected && Percent >= 100;
}
