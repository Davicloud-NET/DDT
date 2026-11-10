// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Net;
using Xunit;

namespace DDT.Server.Tests;

// The web's page answers every path it routes itself, but not one under api/: an agent reads that as JSON.
public sealed class ApiFallbackTests(DdtApplication application) : IClassFixture<DdtApplication>
{
    [Theory]
    [InlineData("/api/no-such-route")]
    [InlineData("/api/agents/no-such-route/console")]
    public async Task AnApiRouteThatIsNotThereIsNotFound(string path)
    {
        using HttpClient client = application.CreateClient();
        using HttpResponseMessage response = await client.GetAsync(path, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.NotEqual("text/html", response.Content.Headers.ContentType?.MediaType);
    }
}
