// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Net;
using DDT.Contracts.Images;
using DDT.Server.Data;
using DDT.Server.Images;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DDT.Server.Tests;

public sealed class ImageUploadSweeperTests(DdtApplication application) : IClassFixture<DdtApplication>
{
    private static readonly TimeSpan s_twoDays = TimeSpan.FromDays(2);

    private ImageStore Store => application.Services.GetRequiredService<ImageStore>();

    private async Task<ImageUploadSession> StartedUploadAsync(SignedInClient administrator)
    {
        byte[] file = TestWim.Create(TestWim.X64);
        ImageUploadSession session = await administrator.StartUploadAsync(file);

        Assert.Equal(HttpStatusCode.NoContent, (await administrator.SendChunkAsync(session.Id, 0, file.AsMemory(0, 1000))).StatusCode);

        return session;
    }

    private async Task LastUpdatedAsync(Guid uploadId, TimeSpan ago)
    {
        using IServiceScope scope = application.Services.CreateScope();
        DdtDbContext database = scope.ServiceProvider.GetRequiredService<DdtDbContext>();
        ImageUpload upload = await database.ImageUploads.SingleAsync(u => u.Id == uploadId, TestContext.Current.CancellationToken);

        upload.UpdatedUtc = DateTimeOffset.UtcNow - ago;
        await database.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    private async Task<bool> ExistsAsync(Guid uploadId)
    {
        using IServiceScope scope = application.Services.CreateScope();
        DdtDbContext database = scope.ServiceProvider.GetRequiredService<DdtDbContext>();

        return await database.ImageUploads.AnyAsync(u => u.Id == uploadId, TestContext.Current.CancellationToken);
    }

    // A part file, or with pathOf another file of an upload that no session has.
    private string OrphanPart(TimeSpan age, Func<Guid, string>? pathOf = null)
    {
        string path = (pathOf ?? Store.PartPath)(Guid.NewGuid());

        Directory.CreateDirectory(Store.UploadsDirectory);
        File.WriteAllBytes(path, new byte[10]);
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow - age);

        return path;
    }

    [Fact]
    public async Task RemovesAbandonedAndFinishedUploadsAndKeepsTheRest()
    {
        Sessions sessions = await SessionsAsync(await application.AdministratorAsync());
        Leftovers leftovers = LeftoversOf(sessions.Abandoned);
        ImageUploadSweeper sweeper = application.Services.GetRequiredService<ImageUploadSweeper>();
        int removed;

        // A request using the session keeps it, however old it looks.
        Assert.True(application.Services.GetRequiredService<ImageUploadLocks>().TryEnter(sessions.Busy.Id, out ImageUploadLock? held));

        using (held)
        {
            removed = await sweeper.SweepOnceAsync(TestContext.Current.CancellationToken);
        }

        Assert.Equal(5, removed);
        Assert.False(await ExistsAsync(sessions.Abandoned.Id));
        Assert.False(File.Exists(Store.PartPath(sessions.Abandoned.Id)));
        Assert.False(File.Exists(leftovers.AbandonedRaw));
        Assert.False(await ExistsAsync(sessions.Finished.Id));
        Assert.False(File.Exists(leftovers.Orphan));
        Assert.False(File.Exists(leftovers.OrphanRaw));
        Assert.False(File.Exists(leftovers.OrphanCompressed));

        Assert.True(await ExistsAsync(sessions.Active.Id));
        Assert.True(File.Exists(Store.PartPath(sessions.Active.Id)));
        Assert.True(await ExistsAsync(sessions.Busy.Id));
        Assert.True(File.Exists(Store.PartPath(sessions.Busy.Id)));
        Assert.True(await ExistsAsync(sessions.JustFinished.Id));
        Assert.True(File.Exists(leftovers.FreshOrphan));
        Assert.True(File.Exists(leftovers.Foreign));

        // Left for the next pass, which finds it free.
        Assert.Equal(1, await sweeper.SweepOnceAsync(TestContext.Current.CancellationToken));
        Assert.False(await ExistsAsync(sessions.Busy.Id));
    }

    // Abandoned, Busy and Finished were last updated two days ago; Finished and JustFinished completed.
    private async Task<Sessions> SessionsAsync(SignedInClient administrator)
    {
        ImageUploadSession abandoned = await StartedUploadAsync(administrator);
        ImageUploadSession active = await StartedUploadAsync(administrator);
        ImageUploadSession busy = await StartedUploadAsync(administrator);
        await LastUpdatedAsync(abandoned.Id, s_twoDays);
        await LastUpdatedAsync(busy.Id, s_twoDays);

        ImageUploadSession finished = await administrator.UploadAsync(TestWim.Create(TestWim.X64));
        ImageUploadSession justFinished = await administrator.UploadAsync(TestWim.Create(TestWim.X64));
        Assert.Equal(HttpStatusCode.Created, (await administrator.CompleteUploadAsync(finished.Id)).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await administrator.CompleteUploadAsync(justFinished.Id)).StatusCode);
        await LastUpdatedAsync(finished.Id, s_twoDays);

        return new Sessions(abandoned, active, busy, finished, justFinished);
    }

    // Files no session owns, and the raw disk an import of the abandoned upload left.
    private Leftovers LeftoversOf(ImageUploadSession abandoned)
    {
        string orphan = OrphanPart(s_twoDays);
        string freshOrphan = OrphanPart(TimeSpan.FromHours(1));

        // What an import of a disk image that stopped leaves: the raw disk and its compressed copy.
        string orphanRaw = OrphanPart(s_twoDays, Store.RawPath);
        string orphanCompressed = OrphanPart(s_twoDays, Store.CompressedPath);
        string abandonedRaw = Store.RawPath(abandoned.Id);
        File.WriteAllBytes(abandonedRaw, new byte[10]);
        string foreign = Path.Combine(Store.UploadsDirectory, "notes.part");
        File.WriteAllBytes(foreign, new byte[10]);
        File.SetLastWriteTimeUtc(foreign, DateTime.UtcNow - s_twoDays);

        return new Leftovers(orphan, freshOrphan, orphanRaw, orphanCompressed, abandonedRaw, foreign);
    }

    private sealed record Sessions(
        ImageUploadSession Abandoned,
        ImageUploadSession Active,
        ImageUploadSession Busy,
        ImageUploadSession Finished,
        ImageUploadSession JustFinished);

    private sealed record Leftovers(string Orphan, string FreshOrphan, string OrphanRaw, string OrphanCompressed, string AbandonedRaw, string Foreign);
}
