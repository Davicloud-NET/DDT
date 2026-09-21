// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using Microsoft.Extensions.Logging;

namespace DDT.Server.Tests;

// One message a host logged, as RecordingLoggerProvider keeps it.
public sealed record LogEntry(EventId EventId, LogLevel Level, string Message);
