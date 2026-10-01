// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Server.Data;

// DDT:Database names one of these. Sqlite is a file in the store, for one server.
public enum DatabaseProvider
{
    Sqlite,
    PostgreSql,
    SqlServer,
}
