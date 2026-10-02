// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Contracts.BootImage;

public enum BootImageJobKind
{
    Build,
    InstallAdk,

    // A build that a builder made on another PC and sent here
    Upload,
}
