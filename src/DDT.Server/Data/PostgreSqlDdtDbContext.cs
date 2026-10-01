// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using Microsoft.EntityFrameworkCore;

namespace DDT.Server.Data;

public sealed class PostgreSqlDdtDbContext(DbContextOptions<PostgreSqlDdtDbContext> options) : DdtDbContext(options);
