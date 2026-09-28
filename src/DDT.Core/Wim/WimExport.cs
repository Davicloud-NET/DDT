// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Core.Wim;

// One image of a WIM, from 1, exported into a new WIM with its own compression.
public sealed record WimExport(string SourceWimPath, int Index, string DestinationWimPath, WimCompression Compression);
