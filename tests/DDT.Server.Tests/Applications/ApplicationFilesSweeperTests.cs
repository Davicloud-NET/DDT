// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Security.Cryptography;
using DDT.Contracts.Applications;
using DDT.Contracts.Deployments;
using DDT.Server.Applications;
using DDT.Server.Data;
using DDT.Server.Images;
using DDT.Server.Packages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static DDT.Server.Tests.ApplicationRequests;

namespace DDT.Server.Tests.Applications;

// Files uploaded for an application that was never saved are removed a day later.
public sealed class ApplicationFilesSweeperTests(DdtApplication application) : IClassFixture<DdtApplication>
{
    private static readonly TimeSpan s_twoDays = TimeSpan.FromDays(2);

    private ImageStore Store => application.Services.GetRequiredService<ImageStore>();

    private Task<Package?> PackageAsync(Guid id) =>
        application.QueryAsync(database => database.Packages.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id, TestContext.Current.CancellationToken));

    private async Task<ApplicationFiles> UploadedAsync(SignedInClient administrator, TimeSpan age)
    {
        ApplicationUpload upload = await administrator.UploadedApplicationAsync(RandomNumberGenerator.GetBytes(128), $"{Guid.NewGuid():N}.exe");

        using IServiceScope scope = application.Services.CreateScope();
        DdtDbContext database = scope.ServiceProvider.GetRequiredService<DdtDbContext>();
        Package package = await database.Packages.SingleAsync(p => p.Id == upload.Files.PackageId, TestContext.Current.CancellationToken);
        package.UploadedUtc = DateTimeOffset.UtcNow - age;
        await database.SaveChangesAsync(TestContext.Current.CancellationToken);

        return upload.Files;
    }

    [Fact]
    public async Task RemovesOldFilesNoApplicationNamesAndKeepsTheRest()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        ApplicationFiles abandoned = await UploadedAsync(administrator, s_twoDays);
        ApplicationFiles fresh = await UploadedAsync(administrator, TimeSpan.FromHours(1));
        ApplicationFiles named = await UploadedAsync(administrator, s_twoDays);
        ApplicationFiles downloading = await UploadedAsync(administrator, s_twoDays);
        await administrator.CreatedApplicationAsync(Command(Unique("Saved"), named.PackageId));
        string abandonedFile = Store.ObjectPath((await PackageAsync(abandoned.PackageId))!.Sha256);
        Guid run = await application.AddAssignedRunAsync(ArtifactKind.Files, downloading.PackageId, (await PackageAsync(downloading.PackageId))!.Sha256);
        ApplicationFilesSweeper sweeper = application.Services.GetRequiredService<ApplicationFilesSweeper>();

        Assert.Equal(1, await sweeper.SweepOnceAsync(TestContext.Current.CancellationToken));

        Assert.Null(await PackageAsync(abandoned.PackageId));
        Assert.False(File.Exists(abandonedFile));
        Assert.NotNull(await PackageAsync(fresh.PackageId));
        Assert.NotNull(await PackageAsync(named.PackageId));
        Assert.NotNull(await PackageAsync(downloading.PackageId));

        await application.EndRunAsync(run);
        Assert.Equal(1, await sweeper.SweepOnceAsync(TestContext.Current.CancellationToken));
        Assert.Null(await PackageAsync(downloading.PackageId));
    }

    // The page may have uploaded the same file again just now, to save it with an application.
    [Fact]
    public async Task UploadingTheSameFileAgainStartsItsDayAnew()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        byte[] file = RandomNumberGenerator.GetBytes(128);
        ApplicationUpload first = await administrator.UploadedApplicationAsync(file, "again.exe");
        await application.QueryAsync(database => database.Packages
            .Where(p => p.Id == first.Files.PackageId)
            .ExecuteUpdateAsync(p => p.SetProperty(x => x.UploadedUtc, DateTimeOffset.UtcNow - s_twoDays), TestContext.Current.CancellationToken));

        ApplicationUpload second = await administrator.UploadedApplicationAsync(file, "again.exe");
        await application.Services.GetRequiredService<ApplicationFilesSweeper>().SweepOnceAsync(TestContext.Current.CancellationToken);

        Assert.Equal(first.Files, second.Files);
        Assert.NotNull(await PackageAsync(first.Files.PackageId));
    }
}
