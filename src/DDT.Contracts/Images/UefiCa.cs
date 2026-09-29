// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Contracts.Images;

// Microsoft's third-party UEFI CAs, which sign the shims of Linux distributions. A boot file may be signed under one or
// both, and a firmware's signature database may hold either, both or neither. The 2011 CA expired in June 2026, so
// newer shims are signed under the 2023 CA. Firmware only holds that one after an update adds it.
[Flags]
public enum UefiCa
{
    None = 0,
    Microsoft2011 = 1,
    Microsoft2023 = 2,
}
