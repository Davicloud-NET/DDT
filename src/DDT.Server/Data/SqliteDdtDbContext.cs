// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace DDT.Server.Data;

public sealed class SqliteDdtDbContext(DbContextOptions<SqliteDdtDbContext> options) : DdtDbContext(options)
{
    protected override void OnModelCreating(ModelBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        base.OnModelCreating(builder);

        // SQLite has no DateTimeOffset type and only compares it for equality, but the audit log is filtered by time.
        // UTC ticks compare in order and keep every digit. EF Core's own binary converter doesn't.
        builder.Entity<AuditEvent>().Property(a => a.OccurredUtc).HasConversion(new ValueConverter<DateTimeOffset, long>(
            time => time.UtcTicks,
            ticks => new DateTimeOffset(ticks, TimeSpan.Zero)));
    }
}
