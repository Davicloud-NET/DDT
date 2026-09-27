// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.ConsoleProtocol;

// Microsoft's third-party UEFI CAs, which a firmware may trust and a disk image's boot file may be signed under.
[Flags]
public enum MicrosoftUefiCas
{
    None = 0,
    Ca2011 = 1,
    Ca2023 = 2,
}
