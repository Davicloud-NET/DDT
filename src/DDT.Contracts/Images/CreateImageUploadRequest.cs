// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Contracts.Images;

// LastModified is the browser's File.lastModified. With the name and length it recognises the same file selected
// again after a reload, so the upload resumes.
public sealed record CreateImageUploadRequest(string FileName, long Length, long LastModified);
