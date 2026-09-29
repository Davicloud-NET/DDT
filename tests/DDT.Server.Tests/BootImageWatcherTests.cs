// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Threading.Channels;
using DDT.Contracts.BootImage;
using DDT.Server.BootImage;
using DDT.Server.Live;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DDT.Server.Tests;

// The server's clock stands still here. The server only looks at the boot directory when the test advances the clock.
public sealed class BootImageWatcherTests(ManualClockApplication application) : IClassFixture<ManualClockApplication>
{
    [Fact]
    public async Task PushesANewBuildOnceItsDescriptionAppears()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        await using LiveListener live = await LiveListener.StartAsync(application, administrator);
        ChannelReader<BootImageView> pushed = live.Listen<BootImageView>(LiveEvents.BootImageChanged);
        string manifest = application.Services.GetRequiredService<BootImageCatalog>().ManifestPath;

        Directory.CreateDirectory(Path.GetDirectoryName(manifest)!);
        await File.WriteAllTextAsync(manifest, """{ "builtUtc": "2026-09-27T08:15:00Z", "drivers": [], "agentVersion": "0.8.0" }""", TestContext.Current.CancellationToken);
        application.Clock.Advance(BootImageWatcher.Interval);

        BootImageView view = await LiveListener.NextAsync(pushed);

        Assert.Equal("0.8.0", view.Build?.AgentVersion);
        Assert.False(view.Stale);
    }
}
