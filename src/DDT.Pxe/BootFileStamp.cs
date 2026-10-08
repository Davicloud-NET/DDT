// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Pxe;

// What a file was when the server checked its content: its length and when it was last written.
public readonly record struct BootFileStamp(long Length, DateTime WriteTimeUtc);
