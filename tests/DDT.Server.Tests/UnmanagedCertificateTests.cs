// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Net;
using DDT.Server.Authentication;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace DDT.Server.Tests;

// Without both certificate paths, like behind a proxy that ends TLS, Kestrel serves its own certificate.
// DDT then has no certificate to describe and no root to offer.
public sealed class UnmanagedCertificateTests(DdtApplication application) : IClassFixture<DdtApplication>
{
    [Fact]
    public async Task ThereIsNoCertificateToDescribe()
    {
        using SignedInClient viewer = await application.SignInAsync(DdtRoleNames.Viewer);

        using HttpResponseMessage response = await viewer.GetAsync("/api/server/certificate");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task ThereIsNoRootToFetch()
    {
        using HttpClient anonymous = application.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        using HttpResponseMessage response = await anonymous.GetAsync(
            new Uri("/api/about/root-certificate", UriKind.Relative),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task ThereIsNoBannerToEnd()
    {
        SignedInClient administrator = await application.AdministratorAsync();

        using HttpResponseMessage response = await administrator.DeleteAsync("/api/server/certificate/replaced-anchor");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
