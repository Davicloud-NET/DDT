using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using DDT.Contracts.Images;
using DDT.Server.Authentication;
using DDT.Server.Data;
using DDT.Server.Images;
using DDT.Server.Machines;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DDT.Server.Tests;

public sealed class ImageUploadTests(DdtApplication application) : IClassFixture<DdtApplication>
{
    private ImageStore Store => application.Services.GetRequiredService<ImageStore>();

    private static Task<T> ReadAsync<T>(HttpResponseMessage response) => RegisteredMachine.ReadAsync<T>(response);

    private static async Task<string?> TitleAsync(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<ProblemDetails>(TestJson.Options))?.Title;

    private async Task<ImageUpload?> FindUploadAsync(Guid uploadId)
    {
        using IServiceScope scope = application.Services.CreateScope();
        DdtDbContext database = scope.ServiceProvider.GetRequiredService<DdtDbContext>();

        return await database.ImageUploads.AsNoTracking().SingleOrDefaultAsync(u => u.Id == uploadId, TestContext.Current.CancellationToken);
    }

    private async Task<IReadOnlyList<ImageUploadSession>> OpenUploadsAsync(SignedInClient client) =>
        await ReadAsync<IReadOnlyList<ImageUploadSession>>(await client.GetAsync(ImageUploadRequests.Uploads));

    [Fact]
    public async Task FindsTheOpenSessionWhenTheSameFileIsSelectedAgain()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        string fileName = $"install-{Guid.NewGuid():N}.wim";

        HttpResponseMessage created = await administrator.CreateUploadAsync(fileName, 5000, lastModified: 1);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        ImageUploadSession session = await ReadAsync<ImageUploadSession>(created);
        Assert.Equal(new ImageUploadSession(session.Id, fileName, 5000, 1, 0, 8 * 1024 * 1024), session);
        Assert.Equal($"/api/images/uploads/{session.Id}", created.Headers.Location?.OriginalString);

        Assert.Equal(HttpStatusCode.NoContent, (await administrator.SendChunkAsync(session.Id, 0, new byte[1000])).StatusCode);

        HttpResponseMessage found = await administrator.CreateUploadAsync(fileName, 5000, lastModified: 1);
        Assert.Equal(HttpStatusCode.OK, found.StatusCode);
        Assert.Equal(session with { Offset = 1000 }, await ReadAsync<ImageUploadSession>(found));

        // A file with the same name and length but another modification time is another file.
        HttpResponseMessage other = await administrator.CreateUploadAsync(fileName, 5000, lastModified: 2);
        Assert.Equal(HttpStatusCode.Created, other.StatusCode);
        ImageUploadSession otherSession = await ReadAsync<ImageUploadSession>(other);

        IReadOnlyList<ImageUploadSession> open = await OpenUploadsAsync(administrator);
        Assert.Contains(session with { Offset = 1000 }, open);
        Assert.Contains(otherSession, open);
    }

    [Fact]
    public async Task RefusesASessionForAnUnusableFile()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        long capacity = Store.Volume().TotalSize;

        Assert.Equal(HttpStatusCode.BadRequest, (await administrator.CreateUploadAsync("install.wim", 0)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await administrator.CreateUploadAsync("install.wim", -5)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await administrator.CreateUploadAsync("install.wim", capacity + 1)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await administrator.CreateUploadAsync("", 10)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await administrator.CreateUploadAsync(new string('a', 257), 10)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await administrator.CreateUploadAsync("in\0stall.wim", 10)).StatusCode);

        // It fits on the volume, but not beside everything else on it and the margin kept free.
        HttpResponseMessage full = await administrator.CreateUploadAsync("install.wim", capacity);
        Assert.Equal(HttpStatusCode.InsufficientStorage, full.StatusCode);
        Assert.StartsWith("The image store needs", await TitleAsync(full), StringComparison.Ordinal);
    }

    [Fact]
    public async Task OnlyAnAdministratorUploads()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        using SignedInClient operatorClient = await application.SignInAsync(DdtRoleNames.Operator);
        ImageUploadSession session = await administrator.StartUploadAsync(TestWim.Create(TestWim.X64));

        Assert.Equal(HttpStatusCode.Forbidden, (await operatorClient.CreateUploadAsync("install.wim", 10)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await operatorClient.GetAsync(ImageUploadRequests.Uploads)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await operatorClient.SendChunkAsync(session.Id, 0, new byte[10])).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await operatorClient.CompleteUploadAsync(session.Id)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await operatorClient.DeleteAsync($"{ImageUploadRequests.Uploads}/{session.Id}")).StatusCode);

        using HttpClient anonymous = application.CreateClient();
        HttpResponseMessage listed = await anonymous.GetAsync(new Uri(ImageUploadRequests.Uploads, UriKind.Relative), TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Unauthorized, listed.StatusCode);
    }

    [Fact]
    public async Task TakesAChunkOnlyAtTheCommittedOffset()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        byte[] file = TestWim.Create(TestWim.X64);
        ImageUploadSession session = await administrator.StartUploadAsync(file);

        HttpResponseMessage first = await administrator.SendChunkAsync(session.Id, 0, file.AsMemory(0, 1000));
        Assert.Equal(HttpStatusCode.NoContent, first.StatusCode);
        Assert.Equal(1000, ImageUploadRequests.OffsetOf(first));

        // The answer to the first chunk was lost, so the client sends it again.
        HttpResponseMessage replay = await administrator.SendChunkAsync(session.Id, 0, file.AsMemory(0, 1000));
        Assert.Equal(HttpStatusCode.Conflict, replay.StatusCode);
        Assert.Equal(1000, ImageUploadRequests.OffsetOf(replay));

        HttpResponseMessage ahead = await administrator.SendChunkAsync(session.Id, 1500, file.AsMemory(1500, 500));
        Assert.Equal(HttpStatusCode.Conflict, ahead.StatusCode);
        Assert.Equal(1000, ImageUploadRequests.OffsetOf(ahead));

        HttpResponseMessage rest = await administrator.SendChunkAsync(session.Id, 1000, file.AsMemory(1000));
        Assert.Equal(HttpStatusCode.NoContent, rest.StatusCode);
        Assert.Equal(file.Length, ImageUploadRequests.OffsetOf(rest));

        Assert.Equal(HttpStatusCode.Created, (await administrator.CompleteUploadAsync(session.Id)).StatusCode);
        Assert.Equal(file, await File.ReadAllBytesAsync(Store.ObjectPath(TestWim.Sha256(file)), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task RefusesAChunkWithoutAUsableLengthOrOffset()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        SignedInClient administrator = await application.AdministratorAsync();
        ImageUploadSession session = await administrator.StartUploadAsync(new byte[5000]);
        Uri chunk = new($"{ImageUploadRequests.Uploads}/{session.Id}", UriKind.Relative);

        using HttpRequestMessage streamed = new(HttpMethod.Patch, chunk) { Content = new UnknownLengthContent(new byte[100]) };
        streamed.Headers.Add(ImageUploadRequests.UploadOffset, "0");
        Assert.Equal(HttpStatusCode.LengthRequired, (await administrator.SendAsync(streamed, cancellationToken)).StatusCode);

        HttpResponseMessage oversized = await administrator.SendChunkAsync(session.Id, 0, new byte[(8 * 1024 * 1024) + 1]);
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, oversized.StatusCode);
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, (await administrator.SendChunkAsync(session.Id, 0, Array.Empty<byte>())).StatusCode);

        using HttpRequestMessage withoutOffset = new(HttpMethod.Patch, chunk) { Content = new ByteArrayContent(new byte[100]) };
        Assert.Equal(HttpStatusCode.BadRequest, (await administrator.SendAsync(withoutOffset, cancellationToken)).StatusCode);

        using HttpRequestMessage negativeOffset = new(HttpMethod.Patch, chunk) { Content = new ByteArrayContent(new byte[100]) };
        negativeOffset.Headers.Add(ImageUploadRequests.UploadOffset, "-1");
        Assert.Equal(HttpStatusCode.BadRequest, (await administrator.SendAsync(negativeOffset, cancellationToken)).StatusCode);

        Assert.Equal(HttpStatusCode.BadRequest, (await administrator.SendChunkAsync(session.Id, 4500, new byte[501])).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await administrator.SendChunkAsync(Guid.NewGuid(), 0, new byte[100])).StatusCode);

        // Nothing of the refused chunks was kept.
        Assert.Equal(0, (await FindUploadAsync(session.Id))!.Offset);
        Assert.Equal(HttpStatusCode.NoContent, (await administrator.SendChunkAsync(session.Id, 0, new byte[5000])).StatusCode);
    }

    [Fact]
    public async Task StartsOverWhenThePartFileLostCommittedBytes()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        byte[] file = TestWim.Create(TestWim.X64);
        ImageUploadSession session = await administrator.StartUploadAsync(file);

        Assert.Equal(HttpStatusCode.NoContent, (await administrator.SendChunkAsync(session.Id, 0, file.AsMemory(0, 2000))).StatusCode);

        await using (FileStream part = new(Store.PartPath(session.Id), FileMode.Open, FileAccess.Write))
        {
            part.SetLength(500);
        }

        HttpResponseMessage next = await administrator.SendChunkAsync(session.Id, 2000, file.AsMemory(2000));
        Assert.Equal(HttpStatusCode.Conflict, next.StatusCode);
        Assert.Equal(0, ImageUploadRequests.OffsetOf(next));
        Assert.Equal(0, (await FindUploadAsync(session.Id))!.Offset);

        Assert.Equal(HttpStatusCode.NoContent, (await administrator.SendChunkAsync(session.Id, 0, file)).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await administrator.CompleteUploadAsync(session.Id)).StatusCode);
        Assert.Equal(file, await File.ReadAllBytesAsync(Store.ObjectPath(TestWim.Sha256(file)), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task CompletesAWimIntoOneLibraryEntryPerX64Image()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        SignedInClient administrator = await application.AdministratorAsync();
        byte[] file = TestWim.Create(TestWim.X64, TestWim.Arm64, TestWim.X64, null);
        string sha256 = TestWim.Sha256(file);
        ImageUploadSession session = await administrator.UploadAsync(file);

        HttpResponseMessage completed = await administrator.CompleteUploadAsync(session.Id);
        Assert.Equal(HttpStatusCode.Created, completed.StatusCode);

        IReadOnlyList<ImageSummary> added = await ReadAsync<IReadOnlyList<ImageSummary>>(completed);
        Assert.Equal([1, 3], added.Select(i => i.WimIndex).Order());

        ImageSummary first = Assert.Single(added, i => i.WimIndex == 1);
        Assert.Equal("Windows 11 Edition1", first.Name);
        Assert.Equal(ImageKind.Wim, first.Kind);
        Assert.Equal(sha256, first.Sha256);
        Assert.Equal(file.Length, first.SizeBytes);
        Assert.Equal("Edition1", first.Edition);
        Assert.Equal("x64", first.Architecture);
        Assert.Equal("10.0.26100.1742", first.Version);
        Assert.Equal("de-DE", first.Language);
        Assert.Equal(9000, first.InstalledBytes);
        Assert.Equal(session.FileName, first.OriginalFileName);
        Assert.NotNull(first.UploadedBy);

        IReadOnlyList<ImageSummary> library = await ReadAsync<IReadOnlyList<ImageSummary>>(await administrator.GetAsync("/api/images"));
        Assert.Equal(added.Select(i => i.Id).Order(), library.Where(i => i.Sha256 == sha256).Select(i => i.Id).Order());

        Assert.Equal(file, await File.ReadAllBytesAsync(Store.ObjectPath(sha256), cancellationToken));
        Assert.False(File.Exists(Store.PartPath(session.Id)));
        Assert.Equal(sha256, (await FindUploadAsync(session.Id))!.CompletedSha256);
        Assert.DoesNotContain(await OpenUploadsAsync(administrator), s => s.Id == session.Id);

        using (IServiceScope scope = application.Services.CreateScope())
        {
            DdtDbContext database = scope.ServiceProvider.GetRequiredService<DdtDbContext>();
            string[] subjects = [.. added.Select(i => i.Id.ToString("D"))];
            List<string?> audited = await database.AuditEvents
                .Where(e => e.Action == AuditActions.ImageUploaded && subjects.Contains(e.SubjectId))
                .Select(e => e.SubjectId)
                .ToListAsync(cancellationToken);

            Assert.Equal(subjects.Order(), audited.Order());
        }

        // A lost answer: completing again gives the same result, and the finished upload takes no more chunks.
        HttpResponseMessage repeated = await administrator.CompleteUploadAsync(session.Id);
        Assert.Equal(HttpStatusCode.OK, repeated.StatusCode);
        Assert.Equal(added.OrderBy(i => i.WimIndex), await ReadAsync<IReadOnlyList<ImageSummary>>(repeated));

        HttpResponseMessage late = await administrator.SendChunkAsync(session.Id, 0, file.AsMemory(0, 100));
        Assert.Equal(HttpStatusCode.Conflict, late.StatusCode);
        Assert.Equal(file.Length, ImageUploadRequests.OffsetOf(late));
    }

    [Fact]
    public async Task AddsNoSecondCopyOfAFileAlreadyInTheLibrary()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        byte[] file = TestWim.Create(TestWim.X64, TestWim.X64);
        string sha256 = TestWim.Sha256(file);

        ImageUploadSession first = await administrator.UploadAsync(file);
        IReadOnlyList<ImageSummary> added = await ReadAsync<IReadOnlyList<ImageSummary>>(await administrator.CompleteUploadAsync(first.Id));

        // The same content under another name.
        ImageUploadSession second = await administrator.UploadAsync(file, fileName: $"copy-{Guid.NewGuid():N}.wim");
        HttpResponseMessage deduplicated = await administrator.CompleteUploadAsync(second.Id);

        Assert.Equal(HttpStatusCode.OK, deduplicated.StatusCode);
        Assert.Equal(added, await ReadAsync<IReadOnlyList<ImageSummary>>(deduplicated));
        Assert.False(File.Exists(Store.PartPath(second.Id)));
        Assert.Equal(sha256, (await FindUploadAsync(second.Id))!.CompletedSha256);

        HttpResponseMessage repeated = await administrator.CompleteUploadAsync(second.Id);
        Assert.Equal(HttpStatusCode.OK, repeated.StatusCode);
        Assert.Equal(added, await ReadAsync<IReadOnlyList<ImageSummary>>(repeated));

        IReadOnlyList<ImageSummary> library = await ReadAsync<IReadOnlyList<ImageSummary>>(await administrator.GetAsync("/api/images"));
        Assert.Equal(2, library.Count(i => i.Sha256 == sha256));

        // The upload added nothing but is recorded once, under the entry of the first index.
        using IServiceScope scope = application.Services.CreateScope();
        DdtDbContext database = scope.ServiceProvider.GetRequiredService<DdtDbContext>();
        string subject = added.Single(i => i.WimIndex == 1).Id.ToString("D");
        List<AuditEvent> audited = await database.AuditEvents
            .Where(e => e.Action == AuditActions.ImageUploaded && e.SubjectId == subject)
            .ToListAsync(TestContext.Current.CancellationToken);

        Assert.Equal(2, audited.Count);
        Assert.Single(audited, e => e.Detail!.StartsWith(second.FileName, StringComparison.Ordinal));
    }

    [Fact]
    public async Task KeepsThePartFileWhenTheNewEntriesCannotBeSaved()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        SignedInClient administrator = await application.AdministratorAsync();
        byte[] file = TestWim.Create(TestWim.X64);
        string sha256 = TestWim.Sha256(file);
        ImageUploadSession session = await administrator.UploadAsync(file);
        ImageUploadCompleter completer = application.Services.GetRequiredService<ImageUploadCompleter>();

        // An uploader with no user row breaks the entries' foreign key, as a user deleted during the upload does.
        UploadCompletion failed = await completer.CompleteAsync(session.Id, Guid.NewGuid(), "gone", null, cancellationToken);

        Assert.Equal(UploadCompletionStatus.Failed, failed.Status);
        Assert.True(File.Exists(Store.PartPath(session.Id)));
        Assert.False(File.Exists(Store.ObjectPath(sha256)));
        Assert.Null((await FindUploadAsync(session.Id))!.CompletedSha256);

        // Completing again, as the answer to the failure asks, needs no chunk sent again.
        Assert.Equal(HttpStatusCode.Created, (await administrator.CompleteUploadAsync(session.Id)).StatusCode);
        Assert.Equal(file, await File.ReadAllBytesAsync(Store.ObjectPath(sha256), cancellationToken));
        Assert.False(File.Exists(Store.PartPath(session.Id)));
    }

    [Fact]
    public async Task PutsBackAStoredFileThatWentMissingUnderItsEntries()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        byte[] file = TestWim.Create(TestWim.X64);
        string sha256 = TestWim.Sha256(file);

        ImageUploadSession first = await administrator.UploadAsync(file);
        Assert.Equal(HttpStatusCode.Created, (await administrator.CompleteUploadAsync(first.Id)).StatusCode);

        File.Delete(Store.ObjectPath(sha256));

        ImageUploadSession again = await administrator.UploadAsync(file, fileName: $"again-{Guid.NewGuid():N}.wim");
        Assert.Equal(HttpStatusCode.OK, (await administrator.CompleteUploadAsync(again.Id)).StatusCode);
        Assert.Equal(file, await File.ReadAllBytesAsync(Store.ObjectPath(sha256), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task RefusesToCompleteBeforeEveryByteArrived()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        byte[] file = TestWim.Create(TestWim.X64);
        ImageUploadSession session = await administrator.StartUploadAsync(file);

        Assert.Equal(HttpStatusCode.NoContent, (await administrator.SendChunkAsync(session.Id, 0, file.AsMemory(0, 3000))).StatusCode);

        HttpResponseMessage early = await administrator.CompleteUploadAsync(session.Id);
        Assert.Equal(HttpStatusCode.Conflict, early.StatusCode);
        Assert.Equal(3000, ImageUploadRequests.OffsetOf(early));
        Assert.Equal(HttpStatusCode.NotFound, (await administrator.CompleteUploadAsync(Guid.NewGuid())).StatusCode);
    }

    [Fact]
    public async Task AsksForTheWholeFileAgainWhenThePartFileIsGone()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        byte[] file = TestWim.Create(TestWim.X64);
        ImageUploadSession session = await administrator.UploadAsync(file);

        File.Delete(Store.PartPath(session.Id));

        HttpResponseMessage lost = await administrator.CompleteUploadAsync(session.Id);
        Assert.Equal(HttpStatusCode.Conflict, lost.StatusCode);
        Assert.Equal(0, ImageUploadRequests.OffsetOf(lost));
        Assert.Equal(0, (await FindUploadAsync(session.Id))!.Offset);

        Assert.Equal(HttpStatusCode.NoContent, (await administrator.SendChunkAsync(session.Id, 0, file)).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await administrator.CompleteUploadAsync(session.Id)).StatusCode);
    }

    [Fact]
    public Task RefusesAFileThatIsNotAWim() =>
        AssertRefusedAsync(RandomNumberGenerator.GetBytes(5000), "This file is not a WIM image.");

    [Fact]
    public Task RefusesASplitWim() =>
        AssertRefusedAsync(
            TestWim.CreateSplit(TestWim.X64),
            "Split WIM files (.swm) are not supported. Export the image into a single WIM first.");

    [Fact]
    public Task RefusesAWimWithoutAnX64Image() =>
        AssertRefusedAsync(TestWim.Create(TestWim.Arm64, TestWim.X86, null), "This WIM holds no x64 Windows image.");

    private async Task AssertRefusedAsync(byte[] file, string reason)
    {
        SignedInClient administrator = await application.AdministratorAsync();
        ImageUploadSession session = await administrator.UploadAsync(file);

        HttpResponseMessage refused = await administrator.CompleteUploadAsync(session.Id);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, refused.StatusCode);
        Assert.Equal(reason, await TitleAsync(refused));

        // The session and its file are gone, so selecting the file again starts a new upload.
        Assert.Null(await FindUploadAsync(session.Id));
        Assert.False(File.Exists(Store.PartPath(session.Id)));
        Assert.Equal(HttpStatusCode.NotFound, (await administrator.SendChunkAsync(session.Id, 0, file.AsMemory(0, 10))).StatusCode);

        // A client whose answer was lost completes again and still learns why.
        HttpResponseMessage repeated = await administrator.CompleteUploadAsync(session.Id);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, repeated.StatusCode);
        Assert.Equal(reason, await TitleAsync(repeated));
    }

    [Fact]
    public async Task AnswersBusyWhileAnotherRequestUsesTheSession()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        byte[] file = TestWim.Create(TestWim.X64);
        ImageUploadSession session = await administrator.StartUploadAsync(file);
        ImageUploadLocks locks = application.Services.GetRequiredService<ImageUploadLocks>();

        Assert.True(locks.TryEnter(session.Id, out ImageUploadLock? held));

        using (held)
        {
            HttpResponseMessage chunk = await administrator.SendChunkAsync(session.Id, 0, file);
            AssertRetryLater(chunk);
            Assert.Equal(0, ImageUploadRequests.OffsetOf(chunk));

            AssertRetryLater(await administrator.CompleteUploadAsync(session.Id));
            AssertRetryLater(await administrator.DeleteAsync($"{ImageUploadRequests.Uploads}/{session.Id}"));
        }

        Assert.Equal(HttpStatusCode.NoContent, (await administrator.SendChunkAsync(session.Id, 0, file)).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await administrator.CompleteUploadAsync(session.Id)).StatusCode);
    }

    [Fact]
    public async Task KeepsCompletingAfterTheRequestGivesUp()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        SignedInClient administrator = await application.AdministratorAsync();
        byte[] file = TestWim.Create(TestWim.X64);
        ImageUploadSession session = await administrator.UploadAsync(file);

        // Holding the library lock stops the completion right before it stores anything.
        await Store.LibraryLock.WaitAsync(cancellationToken);

        try
        {
            using CancellationTokenSource first = new();
            using CancellationTokenSource second = new();
            Task<HttpResponseMessage> one = administrator.CompleteUploadOrGiveUpAsync(session.Id, first.Token);
            Task<HttpResponseMessage> two = administrator.CompleteUploadOrGiveUpAsync(session.Id, second.Token);

            // One request runs the completion; the other finds the session busy.
            Task<HttpResponseMessage> answered = await Task.WhenAny(one, two).WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);
            AssertRetryLater(await answered);

            // The request running it gives up, as a browser behind a proxy's read timeout does.
            await (answered == one ? second : first).CancelAsync();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => answered == one ? two : one);

            AssertRetryLater(await administrator.CompleteUploadAsync(session.Id));
        }
        finally
        {
            Store.LibraryLock.Release();
        }

        // The completion went on without its request, and its stored result answers the next attempt.
        HttpResponseMessage result = await administrator.CompleteUploadAsync(session.Id);

        for (int attempt = 0; attempt < 200 && result.StatusCode == HttpStatusCode.Conflict; attempt++)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(50), cancellationToken);
            result = await administrator.CompleteUploadAsync(session.Id);
        }

        Assert.Equal(HttpStatusCode.OK, result.StatusCode);
        Assert.Equal(TestWim.Sha256(file), Assert.Single(await ReadAsync<IReadOnlyList<ImageSummary>>(result)).Sha256);
    }

    private static void AssertRetryLater(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(new RetryConditionHeaderValue(TimeSpan.FromSeconds(5)), response.Headers.RetryAfter);
    }

    [Fact]
    public async Task DiscardsAnUploadWithItsPartFile()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        byte[] file = TestWim.Create(TestWim.X64);
        ImageUploadSession session = await administrator.StartUploadAsync(file);
        string discard = $"{ImageUploadRequests.Uploads}/{session.Id}";

        Assert.Equal(HttpStatusCode.NoContent, (await administrator.SendChunkAsync(session.Id, 0, file.AsMemory(0, 1000))).StatusCode);
        Assert.True(File.Exists(Store.PartPath(session.Id)));

        Assert.Equal(HttpStatusCode.NoContent, (await administrator.DeleteAsync(discard)).StatusCode);
        Assert.False(File.Exists(Store.PartPath(session.Id)));
        Assert.DoesNotContain(await OpenUploadsAsync(administrator), s => s.Id == session.Id);
        Assert.Equal(HttpStatusCode.NotFound, (await administrator.SendChunkAsync(session.Id, 1000, file.AsMemory(1000, 10))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await administrator.DeleteAsync(discard)).StatusCode);
    }
}
