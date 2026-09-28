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

    // The file wasn't added because of a problem on the server, such as a missing conversion tool or a full volume. The
    // upload stays, so it can be completed again once that's fixed, or discarded.
    Kept,
    Failed,
    Stopping,
}
