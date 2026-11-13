// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Net;
using System.Net.Http.Json;
using DDT.Contracts.Applications;
using DDT.Server.Data;
using DDT.Server.Machines;
using Microsoft.EntityFrameworkCore;
using Xunit;
using static DDT.Server.Tests.ApplicationRequests;

namespace DDT.Server.Tests.Applications;

// Bundles: named, ordered lists of applications in the library.
public sealed class ApplicationBundleTests(DdtApplication application) : IClassFixture<DdtApplication>
{
    [Fact]
    public async Task ABundleKeepsItsMembersInOrder()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        ApplicationView first = await administrator.CreatedApplicationAsync(Command(Unique("Browser")));
        ApplicationView second = await administrator.CreatedApplicationAsync(Command(Unique("Reader")));
        string name = Unique("Office PCs");

        ApplicationBundleView bundle = await administrator.CreatedBundleAsync(Bundle($" {name} ", second.Id, first.Id) with { Description = " Every desk " });

        Assert.Equal(name, bundle.Name);
        Assert.Equal("Every desk", bundle.Description);
        Assert.Equal([second.Id, first.Id], bundle.Members);
        Assert.Empty(bundle.UsedBy);
        Assert.Equal(1, bundle.Revision);
        Assert.Contains(
            await RegisteredMachine.ReadAsync<IReadOnlyList<ApplicationBundleView>>(await administrator.GetAsync(BundlesPath)),
            listed => listed.Id == bundle.Id);
    }

    [Fact]
    public async Task AMemberMustBeAnApplicationInTheLibrary()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        ApplicationView app = await administrator.CreatedApplicationAsync(Command(Unique("Member")));
        ApplicationBundleView other = await administrator.CreatedBundleAsync(Bundle(Unique("Other"), app.Id));

        HttpResponseMessage refused = await administrator.PostAsync(BundlesPath, Bundle(Unique("Nested"), app.Id, other.Id, Guid.NewGuid()));
        HttpResponseMessage empty = await administrator.PostAsync(BundlesPath, Bundle(Unique("Empty")));

        Assert.Equal(["application.gone"], await ErrorCodesAsync(refused, "members[1]"));
        Assert.Equal(["application.gone"], await ErrorCodesAsync(refused, "members[2]"));
        Assert.Equal(["application.bundleMembers"], await ErrorCodesAsync(empty, "members"));
    }

    [Fact]
    public async Task ASaveOnAnOlderRevisionGetsTheCurrentBundle()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        ApplicationView app = await administrator.CreatedApplicationAsync(Command(Unique("Member")));
        ApplicationBundleView bundle = await administrator.CreatedBundleAsync(Bundle(Unique("Editors"), app.Id));
        string renamed = Unique("Renamed");

        ApplicationBundleView saved = await RegisteredMachine.ReadAsync<ApplicationBundleView>(
            await administrator.PutAsync($"{BundlesPath}/{bundle.Id:D}", Bundle(renamed, app.Id) with { Revision = 1 }));
        HttpResponseMessage stale = await administrator.PutAsync($"{BundlesPath}/{bundle.Id:D}", Bundle(Unique("Late"), app.Id) with { Revision = 1 });

        Assert.Equal((2L, renamed), (saved.Revision, saved.Name));
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        Assert.Equal(renamed, (await stale.Content.ReadFromJsonAsync<ApplicationBundleView>(TestJson.Options, TestContext.Current.CancellationToken))!.Name);
        Assert.Equal(HttpStatusCode.NotFound, (await administrator.PutAsync($"{BundlesPath}/{Guid.NewGuid():D}", Bundle(renamed, app.Id))).StatusCode);
    }

    [Fact]
    public async Task DeletingABundleIsAudited()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        ApplicationView app = await administrator.CreatedApplicationAsync(Command(Unique("Member")));
        ApplicationBundleView bundle = await administrator.CreatedBundleAsync(Bundle(Unique("Short lived"), app.Id));
        string subject = bundle.Id.ToString("D");

        Assert.Equal(HttpStatusCode.NoContent, (await administrator.DeleteAsync($"{BundlesPath}/{bundle.Id:D}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await administrator.DeleteAsync($"{BundlesPath}/{bundle.Id:D}")).StatusCode);

        List<AuditEvent> audit = await application.QueryAsync(database => database.AuditEvents
            .AsNoTracking()
            .Where(e => e.SubjectId == subject)
            .OrderBy(e => e.Id)
            .ToListAsync(TestContext.Current.CancellationToken));
        Assert.Equal([AuditActions.ApplicationBundleCreated, AuditActions.ApplicationBundleDeleted], audit.Select(e => e.Action));
        Assert.Empty((await administrator.ApplicationAsync(app.Id)).UsedBy);
    }
}
