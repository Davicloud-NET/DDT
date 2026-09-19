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

        // Once the server has armed its timeout, the time passes without another byte.
        while (!application.Clock.HasTimerDueIn(ImageUploadLimits.NoProgressTimeout))
        {
            await Task.Delay(TimeSpan.FromMilliseconds(10), cancellationToken);
        }

        application.Clock.Advance(ImageUploadLimits.NoProgressTimeout);

        HttpResponseMessage stalled = await sending.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);
        release.SetResult();

        Assert.Equal(HttpStatusCode.RequestTimeout, stalled.StatusCode);
        Assert.Equal(0, ImageUploadRequests.OffsetOf(stalled));

        // The session is free again, and nothing of the chunk that stopped counts.
        Assert.Equal(HttpStatusCode.NoContent, (await administrator.SendChunkAsync(session.Id, 0, file)).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await administrator.CompleteUploadAsync(session.Id)).StatusCode);
    }
}
