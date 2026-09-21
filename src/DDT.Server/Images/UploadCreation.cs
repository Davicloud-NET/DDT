// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Images;

namespace DDT.Server.Images;

// Session is null when the store volume lacks the space; the byte counts then say how much.
public sealed record UploadCreation(ImageUploadSession? Session, bool Created, long RequiredBytes, long AvailableBytes);
