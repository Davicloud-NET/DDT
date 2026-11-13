// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Net;
using DDT.Contracts.Applications;
using DDT.Server.Authentication;
using Xunit;
using static DDT.Server.Tests.ApplicationRequests;

namespace DDT.Server.Tests.Applications;

// Everyone signed in reads the library; only an administrator changes it, by session or API token.
public sealed class ApplicationAccessTests(DdtApplication application) : IClassFixture<DdtApplication>
{
    [Fact]
    public async Task ViewersAndOperatorsReadButDoNotWrite()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        ApplicationView app = await administrator.CreatedApplicationAsync(Command(Unique("Guarded")));
        ApplicationBundleView bundle = await administrator.CreatedBundleAsync(Bundle(Unique("Guarded"), app.Id));

        foreach (string role in new[] { DdtRoleNames.Viewer, DdtRoleNames.Operator })
        {
            using SignedInClient client = await application.SignInAsync(role);

            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(ApplicationsPath)).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(BundlesPath)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsync(ApplicationsPath, Command(Unique("Mine")))).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await client.SaveApplicationAsync(app.Id, Keep(app))).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await client.DeleteAsync($"{ApplicationsPath}/{app.Id:D}")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsync(BundlesPath, Bundle(Unique("Mine"), app.Id))).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await client.PutAsync($"{BundlesPath}/{bundle.Id:D}", Bundle(bundle.Name, app.Id))).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await client.DeleteAsync($"{BundlesPath}/{bundle.Id:D}")).StatusCode);
        }

        Assert.Equal(1, (await administrator.ApplicationAsync(app.Id)).Revision);
    }

    [Fact]
    public async Task AnAdministratorsApiTokenWrites()
    {
        string userName = await application.CreateUserAsync(DdtRoleNames.Administrator);
        (_, string secret) = await application.SeedTokenAsync(userName, DdtRoleNames.Administrator);
        using HttpClient script = application.TokenClient(secret, TestRemoteAddress.Unique());

        HttpResponseMessage created = await script.PostJsonAsync(ApplicationsPath, Command(Unique("Scripted")));
        ApplicationView app = await RegisteredMachine.ReadAsync<ApplicationView>(created);

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await script.GetPathAsync(BundlesPath)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await script.DeletePathAsync($"{ApplicationsPath}/{app.Id:D}")).StatusCode);
    }

    [Fact]
    public async Task NobodySignedInReadsNothing()
    {
        using HttpClient anonymous = application.CreateDefaultClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetPathAsync(ApplicationsPath)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetPathAsync(BundlesPath)).StatusCode);
    }
}
