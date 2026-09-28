// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Core.Wim;

// A directory captured as a new WIM holding one image.
public sealed record WimCapture(string SourceDirectory, string WimPath, string ImageName, WimCompression Compression);
