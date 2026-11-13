// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using DDT.Contracts.Applications;
using DDT.Contracts.Images;
using DDT.Contracts.Packages;
using DDT.Server.Images;
using DDT.Server.Packages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DDT.Server.Tests.Applications;

// An application's files come through the package upload: a zip as it is, any other file zipped by the server.
public sealed class ApplicationUploadTests(DdtApplication application) : IClassFixture<DdtApplication>
{
    private ImageStore Store => application.Services.GetRequiredService<ImageStore>();

    private Task<Package> StoredAsync(Guid packageId) =>
        application.QueryAsync(database => database.Packages.AsNoTracking().SingleAsync(p => p.Id == packageId, TestContext.Current.CancellationToken));

    [Fact]
    public async Task AnMsiIsZippedAsItIsAndSuggestsItsSilentInstall()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        byte[] msi = TestMsi.Fixture();
        string fileName = $"setup-{Guid.NewGuid():N}.msi";
        ImageUploadSession session = await administrator.StartPackageUploadAsync(msi, UploadKind.Application, fileName);
        (await administrator.SendChunkAsync(session.Id, 0, msi)).EnsureSuccessStatusCode();

        HttpResponseMessage completed = await administrator.CompleteUploadAsync(session.Id);
        Assert.Equal(HttpStatusCode.Created, completed.StatusCode);
        ApplicationUpload upload = await RegisteredMachine.ReadAsync<ApplicationUpload>(completed);

        ApplicationSuggestion suggestion = Assert.IsType<ApplicationSuggestion>(upload.Suggestion);
        Assert.Equal("DDT Prüfpaket Größe", suggestion.Name);
        Assert.Equal("Davicloud", suggestion.Publisher);
        Assert.Equal("1.2.3", suggestion.Version);
        Assert.Equal($"msiexec /i \"{fileName}\" /qn /norestart", suggestion.Command);
        Assert.Equal([0], suggestion.SuccessExitCodes);
        Assert.Equal([3010, 1641], suggestion.RestartExitCodes);
        Assert.Equal(new ProductCodeDetection(TestMsi.ProductCode), suggestion.Detection);

        Package stored = await StoredAsync(upload.Files.PackageId);
        Assert.Equal(PackageKind.Application, stored.Kind);
        Assert.Equal(fileName, upload.Files.Name);
        Assert.Equal(new FileInfo(Store.ObjectPath(stored.Sha256)).Length, upload.Files.SizeBytes);
        Assert.Equal(1, stored.FileCount);
        Assert.Equal(msi.Length, stored.ExpandedBytes);

        using (ZipArchive zip = ZipFile.OpenRead(Store.ObjectPath(stored.Sha256)))
        {
            ZipArchiveEntry entry = Assert.Single(zip.Entries);
            Assert.Equal(fileName, entry.FullName);
            Assert.Equal(msi.Length, entry.CompressedLength);

            using MemoryStream content = new();
            await using (Stream data = entry.Open())
            {
                await data.CopyToAsync(content, TestContext.Current.CancellationToken);
            }

            Assert.Equal(msi, content.ToArray());
        }

        Assert.Empty(Directory.EnumerateFileSystemEntries(Store.UploadsDirectory, $"{session.Id:N}*"));

        // The answer to the completion was lost: the browser asks again and gets the files, without the suggestion.
        HttpResponseMessage again = await administrator.CompleteUploadAsync(session.Id);
        Assert.Equal(HttpStatusCode.OK, again.StatusCode);
        Assert.Equal(upload.Files, (await RegisteredMachine.ReadAsync<ApplicationUpload>(again)).Files);
    }

    [Fact]
    public async Task AZipIsKeptAsItIsAndItsOneInstallerAtTheRootSuggestsTheInstall()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        byte[] zip = TestZip.Create(("Setup.MSI", TestMsi.Fixture()), ("docs/readme.txt", RandomNumberGenerator.GetBytes(100)));

        ApplicationUpload upload = await administrator.UploadedApplicationAsync(zip, "tool.zip");

        Assert.Equal("msiexec /i \"Setup.MSI\" /qn /norestart", upload.Suggestion?.Command);
        Assert.Equal(new ProductCodeDetection(TestMsi.ProductCode), upload.Suggestion?.Detection);
        Assert.Equal(zip, await File.ReadAllBytesAsync(Store.ObjectPath((await StoredAsync(upload.Files.PackageId)).Sha256), TestContext.Current.CancellationToken));
        Assert.Equal(zip.Length, upload.Files.SizeBytes);
    }

    [Fact]
    public async Task AZipWithTwoInstallersAtTheRootSuggestsNothing()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        byte[] zip = TestZip.Create(("a.msi", TestMsi.Fixture()), ("b.exe", RandomNumberGenerator.GetBytes(200)));

        ApplicationUpload upload = await administrator.UploadedApplicationAsync(zip, "two.zip");

        Assert.Null(upload.Suggestion);
        Assert.Equal(2, (await StoredAsync(upload.Files.PackageId)).FileCount);
    }

    [Fact]
    public async Task AFileOfAnyOtherKindIsZippedWithoutASuggestion()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        byte[] script = RandomNumberGenerator.GetBytes(300);
        string fileName = $"configure-{Guid.NewGuid():N}.ps1";

        ApplicationUpload upload = await administrator.UploadedApplicationAsync(script, fileName);

        Assert.Null(upload.Suggestion);
        using ZipArchive zip = ZipFile.OpenRead(Store.ObjectPath((await StoredAsync(upload.Files.PackageId)).Sha256));
        Assert.Equal(fileName, Assert.Single(zip.Entries).FullName);
    }

    [Fact]
    public async Task TheSameFileTwiceIsOneSetOfFiles()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        byte[] msi = TestMsi.Fixture();
        string fileName = $"twice-{Guid.NewGuid():N}.msi";

        HttpResponseMessage first = await administrator.UploadPackageAsync(msi, UploadKind.Application, fileName);
        HttpResponseMessage second = await administrator.UploadPackageAsync(msi, UploadKind.Application, fileName);

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        ApplicationUpload added = await RegisteredMachine.ReadAsync<ApplicationUpload>(first);
        ApplicationUpload again = await RegisteredMachine.ReadAsync<ApplicationUpload>(second);
        Assert.Equal(added.Files, again.Files);
        Assert.NotNull(again.Suggestion);

        string sha256 = (await StoredAsync(added.Files.PackageId)).Sha256;
        Assert.Equal(1, await application.QueryAsync(database => database.Packages.CountAsync(p => p.Sha256 == sha256, TestContext.Current.CancellationToken)));
    }

    [Fact]
    public async Task AZipIsCheckedLikeAFilesPackage()
    {
        SignedInClient administrator = await application.AdministratorAsync();

        HttpResponseMessage refused = await administrator.UploadPackageAsync(TestZip.Create("tools/CON.txt"), UploadKind.Application, "bad.zip");

        Assert.Equal(HttpStatusCode.UnprocessableEntity, refused.StatusCode);
    }

    [Fact]
    public async Task ApplicationFilesStayOffThePackagesPage()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        ApplicationUpload upload = await administrator.UploadedApplicationAsync(RandomNumberGenerator.GetBytes(64), "hidden.cmd");
        string package = $"{PackageRequests.Packages}/{upload.Files.PackageId}";

        IReadOnlyList<PackageSummary> listed = await RegisteredMachine.ReadAsync<IReadOnlyList<PackageSummary>>(await administrator.GetAsync(PackageRequests.Packages));

        Assert.DoesNotContain(listed, p => p.Id == upload.Files.PackageId);
        Assert.Equal(HttpStatusCode.NotFound, (await administrator.PutAsync(package, new UpdatePackageRequest("Renamed", null, []))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await administrator.DeleteAsync(package)).StatusCode);
        Assert.Equal(PackageKind.Application, (await StoredAsync(upload.Files.PackageId)).Kind);
    }
}
