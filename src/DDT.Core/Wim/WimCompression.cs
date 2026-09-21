// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Core.Wim;

public enum WimCompression
{
    None,
    Xpress,
    Lzx,

    // Written as a solid resource, the form of Windows Setup ESD files.
    Lzms,
}
