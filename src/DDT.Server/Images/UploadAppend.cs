// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Server.Images;

// Offset is the committed offset after the attempt, which the client continues from.
public sealed record UploadAppend(UploadAppendStatus Status, long Offset);
