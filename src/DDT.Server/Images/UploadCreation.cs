// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Images;

namespace DDT.Server.Images;

// Session is null when the store volume doesn't have enough space. The byte counts then say how much is needed and
// how much is free.
public sealed record UploadCreation(ImageUploadSession? Session, bool Created, long RequiredBytes, long AvailableBytes);
