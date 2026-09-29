// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using DDT.Contracts.Server;
using DDT.Server.Authentication;
using DDT.Server.Data;
using DDT.Server.Machines;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Xunit;

namespace DDT.Server.Tests;

// The first start after an upgrade: the old self-signed certificate is replaced, and administrators are asked to build
// every boot image again with the new root.
public sealed class ServerCertificateEndpointTests(LegacyCertificateApplication application) : IClassFixture<LegacyCertificateApplication>
{
    private const string Certificate = "/api/server/certificate";
    private const string ReplacedAnchor = "/api/server/certificate/replaced-anchor";
    private const string RootCertificate = "/api/about/root-certificate";

    [Fact]
    public async Task TheRebuildBannerStaysUntilAnAdministratorConfirmsIt()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        using SignedInClient viewer = await application.SignInAsync(DdtRoleNames.Viewer);
        using SignedInClient operatorClient = await application.SignInAsync(DdtRoleNames.Operator);
        SignedInClient administrator = await application.AdministratorAsync();

        ServerCertificateView before = await ViewAsync(viewer, cancellationToken);

        Assert.True(before.ManagedByDdt);
        Assert.NotNull(before.AnchorReplacedUtc);
        Assert.Equal(Sha256(File.ReadAllText(application.Files.RootPath)), before.RootSha256);
        Assert.Contains("ddt.lab.example", before.Names);

        using (HttpResponseMessage refused = await operatorClient.DeleteAsync(ReplacedAnchor))
        {
            Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
        }

        using (HttpResponseMessage acknowledged = await administrator.DeleteAsync(ReplacedAnchor))
        {
            Assert.Equal(HttpStatusCode.NoContent, acknowledged.StatusCode);
        }

        Assert.Null((await ViewAsync(viewer, cancellationToken)).AnchorReplacedUtc);

        using (HttpResponseMessage again = await administrator.DeleteAsync(ReplacedAnchor))
        {
            Assert.Equal(HttpStatusCode.NotFound, again.StatusCode);
        }

        string legacy = Sha256(application.Legacy.CertificatePem);
        AuditEvent audit = await application.QueryAsync(database => database.AuditEvents
            .SingleAsync(e => e.Action == AuditActions.CertificateAnchorAcknowledged, cancellationToken));
        Assert.Equal(legacy, audit.SubjectId);
        Assert.NotNull(audit.ActorUserId);
    }

    // The warning is where an administrator finds the root to build boot images with, and its SHA-256 to check it by.
    [Fact]
    public async Task TheUpgradeLogsTheNewRootAndItsSha256()
    {
        using SignedInClient viewer = await application.SignInAsync(DdtRoleNames.Viewer);
        ServerCertificateView view = await ViewAsync(viewer, TestContext.Current.CancellationToken);

        LogEntry migrated = Assert.Single(application.Log.Entries, entry => entry.EventId.Id == 852);
        Assert.Equal(LogLevel.Warning, migrated.Level);
        Assert.Contains(application.Files.RootPath, migrated.Message, StringComparison.Ordinal);
        Assert.Contains(view.RootSha256!, migrated.Message, StringComparison.Ordinal);
    }

    // It's needed before anyone can sign in over a connection their browser trusts.
    [Fact]
    public async Task AnyoneCanFetchTheRootToTrust()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        using HttpClient anonymous = application.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        using HttpResponseMessage response = await anonymous.GetAsync(new Uri(RootCertificate, UriKind.Relative), cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/x-pem-file", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("ddt-root.pem", response.Content.Headers.ContentDisposition?.FileName);
        Assert.Equal(File.ReadAllText(application.Files.RootPath), await response.Content.ReadAsStringAsync(cancellationToken));
    }

    [Fact]
    public async Task OnlySignedInPeopleSeeTheCertificate()
    {
        using HttpClient anonymous = application.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        using HttpResponseMessage response = await anonymous.GetAsync(new Uri(Certificate, UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private static async Task<ServerCertificateView> ViewAsync(SignedInClient client, CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await client.GetAsync(Certificate);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return await response.Content.ReadFromJsonAsync<ServerCertificateView>(TestJson.Options, cancellationToken)
            ?? throw new InvalidOperationException("No certificate view.");
    }

    private static string Sha256(string pem)
    {
        using X509Certificate2 certificate = X509Certificate2.CreateFromPem(pem);

        return certificate.GetCertHashString(HashAlgorithmName.SHA256);
    }
}
