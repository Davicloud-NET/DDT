// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Server.Tests;

// The real host on PostgreSQL or SQL Server instead of the SQLite file.
public sealed class DatabaseServerApplication(TestDatabaseServer server)
    : SettingsApplication(("ConnectionStrings:ddtdb", server.ConnectionString), ("DDT:Database", server.Provider.ToString()));
