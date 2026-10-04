// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Contracts.Import;

// An image file of an MDT deployment share, by its path below the share, with the operating systems MDT lists in it.
public sealed record MdtImageFile(string File, long SizeBytes, bool Found, IReadOnlyList<string> Names);
