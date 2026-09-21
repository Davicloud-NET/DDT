// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Server.Tests;

// The real host on PostgreSQL, which applies every migration at startup instead of creating the schema.
public sealed class PostgresApplication(string connectionString) : SettingsApplication(("ConnectionStrings:ddtdb", connectionString));
