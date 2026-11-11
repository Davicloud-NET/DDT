// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Net;
using System.Threading.Channels;
using DDT.Contracts.Applications;
using DDT.Server.Authentication;
using DDT.Server.Live;
using Xunit;
using static DDT.Server.Tests.ApplicationRequests;

namespace DDT.Server.Tests.Applications;

// Every page that shows the library gets each change from the hub, and an entry whose UsedBy another save changed is
// pushed again.
public sealed class ApplicationLiveTests(DdtApplication application) : IClassFixture<DdtApplication>
{
    [Fact]
    public async Task ChangesReachTheHubWithTheEntriesTheyAffect()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        using SignedInClient viewer = await application.SignInAsync(DdtRoleNames.Viewer);
        await using LiveListener live = await LiveListener.StartAsync(application, viewer);
        ChannelReader<ApplicationView> changed = live.Listen<ApplicationView>(LiveEvents.ApplicationChanged);
        ChannelReader<ApplicationsRemovedEvent> removed = live.Listen<ApplicationsRemovedEvent>(LiveEvents.ApplicationsRemoved);
        ChannelReader<ApplicationBundleView> bundleChanged = live.Listen<ApplicationBundleView>(LiveEvents.ApplicationBundleChanged);
        ChannelReader<ApplicationBundlesRemovedEvent> bundlesRemoved = live.Listen<ApplicationBundlesRemovedEvent>(LiveEvents.ApplicationBundlesRemoved);

        ApplicationView runtime = await administrator.CreatedApplicationAsync(Command(Unique("Runtime")));
        await LiveListener.NextAsync(changed, view => view.Id == runtime.Id);

        ApplicationView app = await administrator.CreatedApplicationAsync(Command(Unique("App"), null, runtime.Id));
        await LiveListener.NextAsync(changed, view => view.Id == app.Id);
        ApplicationView used = await LiveListener.NextAsync(changed, view => view.Id == runtime.Id);
        Assert.Equal([app.Id], used.UsedBy.Select(use => use.Id));

        // A rename shows in the UsedBy of what the application depends on.
        string renamed = Unique("Renamed");
        (await administrator.SaveApplicationAsync(app.Id, Keep(app) with { Name = renamed })).EnsureSuccessStatusCode();
        Assert.Equal(renamed, (await LiveListener.NextAsync(changed, view => view.Id == runtime.Id && view.UsedBy.Any(use => use.Name == renamed))).UsedBy[0].Name);

        ApplicationBundleView bundle = await administrator.CreatedBundleAsync(Bundle(Unique("Base"), runtime.Id));
        await LiveListener.NextAsync(bundleChanged, view => view.Id == bundle.Id);
        await LiveListener.NextAsync(changed, view => view.Id == runtime.Id && view.UsedBy.Count == 2);

        Assert.Equal(HttpStatusCode.NoContent, (await administrator.DeleteAsync($"{BundlesPath}/{bundle.Id:D}")).StatusCode);
        Assert.Equal([bundle.Id], (await LiveListener.NextAsync(bundlesRemoved)).BundleIds);

        Assert.Equal(HttpStatusCode.NoContent, (await administrator.DeleteAsync($"{ApplicationsPath}/{app.Id:D}")).StatusCode);
        Assert.Equal([app.Id], (await LiveListener.NextAsync(removed)).ApplicationIds);
        await LiveListener.NextAsync(changed, view => view.Id == runtime.Id && view.UsedBy.Count == 0);
    }
}
