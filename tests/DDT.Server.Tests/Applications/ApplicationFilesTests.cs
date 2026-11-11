// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Net;
using System.Security.Cryptography;
using DDT.Contracts.Applications;
using DDT.Contracts.Deployments;
using DDT.Contracts.Images;
using DDT.Contracts.Packages;
using DDT.Server.Images;
using DDT.Server.Packages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static DDT.Server.Tests.ApplicationRequests;

namespace DDT.Server.Tests.Applications;

// Deleting an application, and what happens to its files: they go with the last application that names them, and the
// stored file goes when nothing else uses it.
public sealed class ApplicationFilesTests(DdtApplication application) : IClassFixture<DdtApplication>
{
    private ImageStore Store => application.Services.GetRequiredService<ImageStore>();

    private Task<Package?> PackageAsync(Guid id) =>
        application.QueryAsync(database => database.Packages.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id, TestContext.Current.CancellationToken));

    private async Task<string> StoredFileAsync(ApplicationFiles files) => Store.ObjectPath((await PackageAsync(files.PackageId))!.Sha256);

    private async Task<(ApplicationFiles Files, ApplicationView Application)> WithFilesAsync(SignedInClient administrator, string name)
    {
        ApplicationUpload upload = await administrator.UploadedApplicationAsync(RandomNumberGenerator.GetBytes(256), $"{Guid.NewGuid():N}.exe");

        return (upload.Files, await administrator.CreatedApplicationAsync(Command(Unique(name), upload.Files.PackageId)));
    }

    [Fact]
    public async Task ADeleteIsRefusedWhileABundleOrAnotherApplicationNamesIt()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        ApplicationView runtime = await administrator.CreatedApplicationAsync(Command(Unique("Runtime")));
        ApplicationView app = await administrator.CreatedApplicationAsync(Command(Unique("App"), null, runtime.Id));
        ApplicationBundleView bundle = await administrator.CreatedBundleAsync(Bundle(Unique("Base"), runtime.Id));

        HttpResponseMessage refused = await administrator.DeleteAsync($"{ApplicationsPath}/{runtime.Id:D}");

        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        Assert.Equal($"This application is used by {bundle.Name}, {app.Name}. Take it out there first.", await TestDatabase.TitleAsync(refused));

        Assert.Equal(HttpStatusCode.NoContent, (await administrator.DeleteAsync($"{BundlesPath}/{bundle.Id:D}")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await administrator.DeleteAsync($"{ApplicationsPath}/{app.Id:D}")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await administrator.DeleteAsync($"{ApplicationsPath}/{runtime.Id:D}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await administrator.DeleteAsync($"{ApplicationsPath}/{runtime.Id:D}")).StatusCode);
    }

    [Fact]
    public async Task DeletingAnApplicationRemovesItsFilesAndTheStoredFile()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        (ApplicationFiles files, ApplicationView app) = await WithFilesAsync(administrator, "Doomed");
        string stored = await StoredFileAsync(files);

        Assert.Equal(HttpStatusCode.NoContent, (await administrator.DeleteAsync($"{ApplicationsPath}/{app.Id:D}")).StatusCode);

        Assert.Null(await PackageAsync(files.PackageId));
        Assert.False(File.Exists(stored));
    }

    [Fact]
    public async Task TheStoredFileStaysWhileAnotherPackageSharesIt()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        byte[] zip = TestZip.Create("setup.cmd");
        PackageSummary filesPackage = await administrator.UploadedPackageAsync(zip, UploadKind.Files, "shared.zip");
        ApplicationUpload upload = await administrator.UploadedApplicationAsync(zip, "shared.zip");
        ApplicationView app = await administrator.CreatedApplicationAsync(Command(Unique("Sharing"), upload.Files.PackageId));

        Assert.Equal(HttpStatusCode.NoContent, (await administrator.DeleteAsync($"{ApplicationsPath}/{app.Id:D}")).StatusCode);

        Assert.Null(await PackageAsync(upload.Files.PackageId));
        Assert.True(File.Exists(Store.ObjectPath(filesPackage.Sha256)));
    }

    [Fact]
    public async Task FilesTwoApplicationsNameStayUntilTheLastLetsGo()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        (ApplicationFiles files, ApplicationView first) = await WithFilesAsync(administrator, "First");
        ApplicationView second = await administrator.CreatedApplicationAsync(Command(Unique("Second"), files.PackageId));

        Assert.Equal(HttpStatusCode.NoContent, (await administrator.DeleteAsync($"{ApplicationsPath}/{first.Id:D}")).StatusCode);
        Assert.NotNull(await PackageAsync(files.PackageId));

        Assert.Equal(HttpStatusCode.NoContent, (await administrator.DeleteAsync($"{ApplicationsPath}/{second.Id:D}")).StatusCode);
        Assert.Null(await PackageAsync(files.PackageId));
    }

    [Fact]
    public async Task ReplacedFilesAreRemoved()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        (ApplicationFiles old, ApplicationView app) = await WithFilesAsync(administrator, "Updated");
        string stored = await StoredFileAsync(old);
        ApplicationUpload replacement = await administrator.UploadedApplicationAsync(RandomNumberGenerator.GetBytes(256), "new.exe");

        HttpResponseMessage saved = await administrator.SaveApplicationAsync(app.Id, Keep(app) with { PackageId = replacement.Files.PackageId });

        Assert.Equal(replacement.Files, (await RegisteredMachine.ReadAsync<ApplicationView>(saved)).Files);
        Assert.Null(await PackageAsync(old.PackageId));
        Assert.False(File.Exists(stored));
    }

    // A machine waiting to download the files would fail at the step, after its disk was erased.
    [Fact]
    public async Task FilesARunStillDownloadsAreKept()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        (ApplicationFiles files, ApplicationView app) = await WithFilesAsync(administrator, "Busy");
        Guid run = await application.AddAssignedRunAsync(ArtifactKind.Files, files.PackageId, (await PackageAsync(files.PackageId))!.Sha256);
        ApplicationUpload replacement = await administrator.UploadedApplicationAsync(RandomNumberGenerator.GetBytes(256), "other.exe");

        HttpResponseMessage deleted = await administrator.DeleteAsync($"{ApplicationsPath}/{app.Id:D}");
        HttpResponseMessage replaced = await administrator.SaveApplicationAsync(app.Id, Keep(app) with { PackageId = replacement.Files.PackageId });

        Assert.Equal(HttpStatusCode.Conflict, deleted.StatusCode);
        Assert.Equal("application.filesInUse", await CodeAsync(deleted));
        Assert.Equal(HttpStatusCode.Conflict, replaced.StatusCode);
        Assert.Equal("application.filesInUse", await CodeAsync(replaced));

        await application.EndRunAsync(run);
        Assert.Equal(HttpStatusCode.NoContent, (await administrator.DeleteAsync($"{ApplicationsPath}/{app.Id:D}")).StatusCode);
        Assert.Null(await PackageAsync(files.PackageId));
    }
}
