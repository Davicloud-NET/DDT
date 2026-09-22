// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DotNet.Testcontainers.Builders;
using Testcontainers.PostgreSql;
using Xunit;

namespace DDT.Server.Tests;

internal static class TestPostgres
{
    // Null without Docker, and the test that asked skips.
    public static async Task<PostgreSqlContainer?> StartAsync()
    {
        PostgreSqlContainer? container = null;

        try
        {
            container = new PostgreSqlBuilder("postgres:17-alpine").Build();
            await container.StartAsync(TestContext.Current.CancellationToken);

            return container;
        }
        catch (DockerUnavailableException)
        {
            if (container is not null)
            {
                await container.DisposeAsync();
            }

            return null;
        }
    }
}
