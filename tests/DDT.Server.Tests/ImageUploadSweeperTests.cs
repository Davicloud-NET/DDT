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

    private string OrphanPart(TimeSpan age)
    {
        string path = Store.PartPath(Guid.NewGuid());

        Directory.CreateDirectory(Store.UploadsDirectory);
        File.WriteAllBytes(path, new byte[10]);
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow - age);

        return path;
    }

    [Fact]
    public async Task RemovesAbandonedAndFinishedUploadsAndKeepsTheRest()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        ImageUploadLocks locks = application.Services.GetRequiredService<ImageUploadLocks>();

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

        string orphan = OrphanPart(s_twoDays);
        string freshOrphan = OrphanPart(TimeSpan.FromHours(1));
        string foreign = Path.Combine(Store.UploadsDirectory, "notes.part");
        File.WriteAllBytes(foreign, new byte[10]);
        File.SetLastWriteTimeUtc(foreign, DateTime.UtcNow - s_twoDays);

        int removed;

        // A request using the session keeps it, however old it looks.
        Assert.True(locks.TryEnter(busy.Id, out ImageUploadLock? held));

        using (held)
        {
            removed = await application.Services.GetRequiredService<ImageUploadSweeper>()
                .SweepOnceAsync(TestContext.Current.CancellationToken);
        }

        Assert.Equal(3, removed);
        Assert.False(await ExistsAsync(abandoned.Id));
        Assert.False(File.Exists(Store.PartPath(abandoned.Id)));
        Assert.False(await ExistsAsync(finished.Id));
        Assert.False(File.Exists(orphan));

        Assert.True(await ExistsAsync(active.Id));
        Assert.True(File.Exists(Store.PartPath(active.Id)));
        Assert.True(await ExistsAsync(busy.Id));
        Assert.True(File.Exists(Store.PartPath(busy.Id)));
        Assert.True(await ExistsAsync(justFinished.Id));
        Assert.True(File.Exists(freshOrphan));
        Assert.True(File.Exists(foreign));

        // Left for the next pass, which finds it free.
        Assert.Equal(1, await application.Services.GetRequiredService<ImageUploadSweeper>()
            .SweepOnceAsync(TestContext.Current.CancellationToken));
        Assert.False(await ExistsAsync(busy.Id));
    }
}
