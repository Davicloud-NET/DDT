// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Contracts.Import;

// A WIM, ESD or ISO below one of the import folders.
public sealed record ImportFile(string Path, string Name, long SizeBytes, DateTimeOffset ModifiedUtc);
