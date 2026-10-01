// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Contracts.Server;

// One of the server's log files, for the Server page to download.
public sealed record LogFileView(string Name, long Size, DateTimeOffset WrittenUtc);
