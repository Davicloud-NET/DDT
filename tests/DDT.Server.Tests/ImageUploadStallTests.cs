// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Diagnostics;
using System.Net;
using DDT.Contracts.Images;
using DDT.Server.Images;
using Xunit;

namespace DDT.Server.Tests;

public sealed class ImageUploadStallTests(ManualClockApplication application) : IClassFixture<ManualClockApplication>
{
    [Fact]
    public async Task GivesUpOnAChunkThatStopsArrivingAndFreesTheSession()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        SignedInClient administrator = await application.AdministratorAsync();
        byte[] file = TestWim.Create(TestWim.X64);
        ImageUploadSession session = await administrator.StartUploadAsync(file);
        TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);

        using HttpRequestMessage request = new(HttpMethod.Patch, new Uri($"{ImageUploadRequests.Uploads}/{session.Id}", UriKind.Relative))
        {
            Content = new StallingContent(file, 1000, release.Task),
        };

        request.Headers.Add(ImageUploadRequests.UploadOffset, "0");

        Task<HttpResponseMessage> sending = administrator.SendAsync(request, cancellationToken);

        // Once the server has armed its timeout, the clock moves past it without another byte arriving.
        while (!application.Clock.HasTimerDueIn(ImageUploadLimits.NoProgressTimeout))
        {
            await Task.Delay(TimeSpan.FromMilliseconds(10), cancellationToken);
        }

        application.Clock.Advance(ImageUploadLimits.NoProgressTimeout);

        HttpResponseMessage stalled = await sending.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);
        release.SetResult();

        Assert.Equal(HttpStatusCode.RequestTimeout, stalled.StatusCode);
        Assert.Equal(0, ImageUploadRequests.OffsetOf(stalled));

        // The session is free again, and nothing from the stalled chunk counts.
        Assert.Equal(HttpStatusCode.NoContent, (await administrator.SendChunkAsync(session.Id, 0, file)).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await administrator.CompleteUploadAsync(session.Id)).StatusCode);
    }

    // The timeout counts from the last bytes that arrived, not from the start of the chunk, so a slow link that keeps
    // carrying data gets its chunk through.
    [Fact]
    public async Task KeepsAChunkThatPausesButKeepsArriving()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        SignedInClient administrator = await application.AdministratorAsync();
        byte[] file = TestWim.Create(TestWim.X64);
        ImageUploadSession session = await administrator.StartUploadAsync(file);
        TaskCompletionSource firstPause = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource secondPause = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TimeSpan pause = TimeSpan.FromSeconds(20);

        using HttpRequestMessage request = new(HttpMethod.Patch, new Uri($"{ImageUploadRequests.Uploads}/{session.Id}", UriKind.Relative))
        {
            Content = new PausingContent(file, 1000, [firstPause.Task, secondPause.Task]),
        };

        request.Headers.Add(ImageUploadRequests.UploadOffset, "0");

        Task<HttpResponseMessage> sending = administrator.SendAsync(request, cancellationToken);

        await WaitForTheTimeoutAsync(cancellationToken);
        application.Clock.Advance(pause);
        firstPause.SetResult();

        // The second part restarts the timeout, so it falls due a whole timeout from now, not ten seconds from now.
        await WaitForTheTimeoutAsync(cancellationToken);
        application.Clock.Advance(pause);
        secondPause.SetResult();

        using HttpResponseMessage response = await sending.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(file.Length, ImageUploadRequests.OffsetOf(response));
    }

    // Bounded, so a server that never restarts its timeout fails the test instead of hanging it.
    private async Task WaitForTheTimeoutAsync(CancellationToken cancellationToken)
    {
        Stopwatch waited = Stopwatch.StartNew();

        while (!application.Clock.HasTimerDueIn(ImageUploadLimits.NoProgressTimeout))
        {
            Assert.True(waited.Elapsed < TimeSpan.FromSeconds(10), "The server did not restart its no-progress timeout when data arrived.");
            await Task.Delay(TimeSpan.FromMilliseconds(10), cancellationToken);
        }
    }
}
