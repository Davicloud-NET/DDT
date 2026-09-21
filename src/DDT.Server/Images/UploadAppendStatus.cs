// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Server.Images;

public enum UploadAppendStatus
{
    Appended,
    NotFound,
    BeyondLength,
    Completed,
    Busy,
    OffsetMismatch,
    Restarted,
    CutOff,
    Stalled,
    DiskFull,
}
