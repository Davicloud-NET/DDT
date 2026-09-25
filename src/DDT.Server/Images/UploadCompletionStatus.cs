// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Server.Images;

public enum UploadCompletionStatus
{
    Added,
    Existing,
    NotFound,
    Busy,
    Incomplete,
    Refused,

    // Not added for a cause on the server, such as a missing conversion tool or a full volume. The upload stays, to be
    // completed again once that is fixed, or discarded.
    Kept,
    Failed,
    Stopping,
}
