// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography.X509Certificates;
using DDT.Contracts.Settings;
using DDT.Server.Certificates;
using DDT.Server.Machines;
using DDT.Server.Settings;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DDT.Server.Tests;

// The certificate on the settings page. The test server has no TLS, so no request here was served a pair and none can
// confirm one; ProvisionalCertificateTests does that on a real Kestrel.
public sealed class SettingsCertificateTests(SettingsCertificateTests.CertificateApplication application)
    : IClassFixture<SettingsCertificateTests.CertificateApplication>
{
    [Fact]
    public async Task TheServerNamesAreSeededFromConfigurationButNeverLocked()
    {
        SignedInClient administrator = await application.AdministratorAsync();

        CertificateView view = await RegisteredMachine.ReadAsync<CertificateView>(await administrator.GetAsync("/api/settings/certificate"));

        Assert.True(view.Manageable);
        Assert.True(view.CanGenerate);
        Assert.True(view.HasRoot);
        Assert.Null(view.ServedHere);
        Assert.Contains("ddt.corp.example", view.Names.Values.SubjectAlternativeNames);
        Assert.Empty(view.Names.Locked);
        Assert.Contains("subjectAlternativeNames", view.Names.Reauthenticate);
        Assert.Contains("ddt.corp.example", view.Served!.Names);
    }

    [Fact]
    public async Task GenerateServesAPairFromTheRootProvisionally()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        string token = await administrator.TokenAsync();

        HttpResponseMessage response = await Post(administrator, "/api/settings/certificate/generate", new CertificateGenerate(null), token);
        CertificateView view = await RegisteredMachine.ReadAsync<CertificateView>(response);

        Assert.NotNull(view.ProvisionalUntil);
        Assert.True(view.Served!.ManagedByDdt);
        Assert.True(await application.QueryAsync(database => database.AuditEvents.AnyAsync(
            audit => audit.Action == AuditActions.CertificateReplaced && audit.Detail!.Contains("Generated the server certificate"),
            TestContext.Current.CancellationToken)));

        // No connection of the test server was served it, so none proves that a browser accepts it.
        HttpResponseMessage confirm = await administrator.PostAsync("/api/settings/certificate/confirm");
        Assert.Equal(HttpStatusCode.Conflict, confirm.StatusCode);
        Assert.StartsWith("This connection was served the certificate before the new one", await TestDatabase.TitleAsync(confirm), StringComparison.Ordinal);
    }

    // Boot images pin DDT's root, so a certificate from another CA needs the confirmation certificate.newRoot.
    [Fact]
    public async Task AnUploadFromAnotherCaIsSavedOnlyOnceConfirmed()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        string token = await administrator.TokenAsync();
        DateTimeOffset now = DateTimeOffset.UtcNow;
        PemPair authority = AdministratorCertificate.CreateAuthority("CN=Example CA", null, now.AddDays(-2), now.AddYears(2));
        PemPair pair = AdministratorCertificate.Issue(authority, "CN=ddt.corp.example", ["localhost", "ddt.corp.example"], now.AddDays(-1), now.AddDays(90));
        CertificateUpload upload = new(pair.CertificatePem + Environment.NewLine + authority.CertificatePem, pair.KeyPem, null, null, null);

        HttpResponseMessage unconfirmed = await Post(administrator, "/api/settings/certificate", upload, token);

        Assert.Equal(HttpStatusCode.BadRequest, unconfirmed.StatusCode);
        Assert.Equal([SettingWarningCodes.CertificateNewRoot], await SettingsRequests.UnconfirmedAsync(unconfirmed));

        CertificateView view = await RegisteredMachine.ReadAsync<CertificateView>(
            await Post(administrator, "/api/settings/certificate", upload with { Confirm = [SettingWarningCodes.CertificateNewRoot] }, token));

        Assert.Equal("CN=ddt.corp.example", view.Served!.Subject);
        Assert.False(view.Served.ManagedByDdt);
        Assert.NotNull(view.ProvisionalUntil);
        Assert.Contains("-----BEGIN CERTIFICATE-----", File.ReadAllText(application.Files.CertificatePath), StringComparison.Ordinal);
        Assert.True(File.Exists(application.Files.PreviousCertificatePath));
    }

    [Theory]
    [InlineData("missing-name")]
    [InlineData("expired")]
    [InlineData("wrong-key")]
    public async Task AnUploadThatBrowsersOrAgentsWouldRefuseIsRefused(string mistake)
    {
        SignedInClient administrator = await application.AdministratorAsync();
        DateTimeOffset now = DateTimeOffset.UtcNow;
        PemPair authority = AdministratorCertificate.CreateAuthority("CN=Example CA", null, now.AddDays(-400), now.AddYears(2));
        string[] names = mistake == "missing-name" ? ["localhost"] : ["localhost", "ddt.corp.example"];
        PemPair pair = AdministratorCertificate.Issue(authority, "CN=ddt", names, now.AddDays(-300), mistake == "expired" ? now.AddDays(-1) : now.AddDays(90));
        PemPair other = AdministratorCertificate.Issue(authority, "CN=other", names, now.AddDays(-1), now.AddDays(90));
        CertificateUpload upload = new(pair.CertificatePem, mistake == "wrong-key" ? other.KeyPem : pair.KeyPem, null, null, [SettingWarningCodes.CertificateNewRoot]);

        HttpResponseMessage response = await Post(administrator, "/api/settings/certificate", upload, await administrator.TokenAsync());

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        IDictionary<string, string[]> errors = (await SettingsRequests.ProblemsAsync(response)).Errors;
        Assert.Contains(mistake == "wrong-key" ? "keyPem" : "certificate", errors.Keys);

        if (mistake == "missing-name")
        {
            Assert.Contains("ddt.corp.example", Assert.Single(errors["certificate"]), StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task ChangingTheCertificateNeedsAFreshProofOfIdentity()
    {
        SignedInClient administrator = await application.AdministratorAsync();

        Assert.Equal(HttpStatusCode.Forbidden, (await Post(administrator, "/api/settings/certificate/generate", new CertificateGenerate(null), null)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Post(administrator, "/api/settings/certificate", new CertificateUpload("x", "y", null, null, null), null)).StatusCode);
    }

    [Fact]
    public async Task TheServerNamesAreSavedLikeASection()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        CertificateView before = await RegisteredMachine.ReadAsync<CertificateView>(await administrator.GetAsync("/api/settings/certificate"));
        CertificateSettings names = new([.. before.Names.Values.SubjectAlternativeNames, "ddt2.corp.example"]);

        Assert.Equal(HttpStatusCode.Forbidden, (await administrator.SaveAsync(SettingsSectionNames.Certificate + "/names", before.Names.Version, names)).StatusCode);

        SettingsSectionView<CertificateSettings> saved = await RegisteredMachine.ReadAsync<SettingsSectionView<CertificateSettings>>(
            await administrator.SaveAsync(SettingsSectionNames.Certificate + "/names", before.Names.Version, names, reauthentication: await administrator.TokenAsync()));

        Assert.Contains("ddt2.corp.example", saved.Values.SubjectAlternativeNames);

        HttpResponseMessage invalid = await administrator.SaveAsync(
            SettingsSectionNames.Certificate + "/names",
            saved.Version,
            new CertificateSettings(["not a name!"]),
            reauthentication: await administrator.TokenAsync());
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        Assert.Contains("subjectAlternativeNames", (await SettingsRequests.ProblemsAsync(invalid)).Errors.Keys);

        await administrator.SaveAsync(
            SettingsSectionNames.Certificate + "/names",
            saved.Version,
            before.Names.Values,
            reauthentication: await administrator.TokenAsync());
    }

    // A PFX, a key under a password or TLS at a proxy is managed by hand.
    [Fact]
    public async Task WithoutBothPemFilesThePageDoesNotManageTheCertificate()
    {
        using DdtApplication plain = new();
        SignedInClient administrator = await plain.AdministratorAsync();

        CertificateView view = await RegisteredMachine.ReadAsync<CertificateView>(await administrator.GetAsync("/api/settings/certificate"));

        Assert.False(view.Manageable);
        Assert.StartsWith("The page manages the certificate only when", view.NotManageable, StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.Conflict, (await Post(administrator, "/api/settings/certificate/generate", new CertificateGenerate(null), null)).StatusCode);
    }

    private static async Task<HttpResponseMessage> Post(SignedInClient client, string path, object body, string? token)
    {
        using HttpRequestMessage request = new(HttpMethod.Post, new Uri(path, UriKind.Relative))
        {
            Content = JsonContent.Create(body, options: TestJson.Options),
        };

        if (token is not null)
        {
            request.Headers.Add(ReauthenticationTokens.HeaderName, token);
        }

        return await client.SendAsync(request, TestContext.Current.CancellationToken);
    }

    // The certificate in the store's certs folder, as the container keeps it, with a name for the server.
    public sealed class CertificateApplication : DdtApplication
    {
        public CertificateFiles Files => new(Path.Combine(StorePath, "certs", "ddt.pem"), Path.Combine(StorePath, "certs", "ddt-key.pem"));

        protected override void ConfigureTestHost(IWebHostBuilder builder)
        {
            ArgumentNullException.ThrowIfNull(builder);

            builder.UseSetting("Kestrel:Certificates:Default:Path", Files.CertificatePath);
            builder.UseSetting("Kestrel:Certificates:Default:KeyPath", Files.KeyPath);
            builder.UseSetting("DDT:Https:SubjectAlternativeNames", "ddt.corp.example");
        }
    }
}
