// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using DDT.Contracts.Images;
using DDT.Contracts.Import;
using DDT.Contracts.Packages;
using DDT.Server.Images;
using DDT.Server.Import;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static DDT.Server.Tests.AccountRequests;

namespace DDT.Server.Tests.WindowsPackages;

// A Windows package comes through the package upload: a .msu or .cab zipped by the server, a zip of them, or the
// Languages and Optional Features ISO, which goes into the import folder.
public sealed class WindowsPackageUploadTests(DdtApplication application) : IClassFixture<DdtApplication>
{
    private const string Update = "windows11.0-kb5132061-x64_aedc878e9e078df2ec7e2cfb98dd5b21831cdbcc.msu";
    private const string Checkpoint = "windows11.0-kb5043080-x64_953449672073f8fb99badb4cc6d5d7849b9c83e8.msu";

    private ImageStore Store => application.Services.GetRequiredService<ImageStore>();

    [Fact]
    public async Task AnMsuIsZippedAsItIsAndReadFromItsName()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        byte[] msu = RandomNumberGenerator.GetBytes(4096);

        HttpResponseMessage completed = await administrator.UploadPackageAsync(msu, UploadKind.WindowsPackage, Update);

        Assert.Equal(HttpStatusCode.Created, completed.StatusCode);
        PackageSummary package = await RegisteredMachine.ReadAsync<PackageSummary>(completed);
        Assert.Equal((PackageKind.WindowsPackage, 1, msu.Length), (package.Kind, package.FileCount, package.ExpandedBytes));
        Assert.Equal(WindowsPackageContent.Update, package.Windows?.Content);
        Assert.Equal([5132061], package.Windows?.KbNumbers);
        Assert.Equal("x64", package.Windows?.Architecture);

        using ZipArchive zip = ZipFile.OpenRead(Store.ObjectPath(package.Sha256));
        ZipArchiveEntry entry = Assert.Single(zip.Entries);
        Assert.Equal((Update, (long)msu.Length), (entry.FullName, entry.CompressedLength));
    }

    [Fact]
    public async Task AZipOfAnUpdateAndItsCheckpointListsBoth()
    {
        SignedInClient administrator = await application.AdministratorAsync();

        PackageSummary package = await administrator.UploadedPackageAsync(TestZip.Create(Update, Checkpoint), UploadKind.WindowsPackage, "cu.zip");

        Assert.Equal(WindowsPackageContent.Update, package.Windows?.Content);
        Assert.Equal([5132061, 5043080], package.Windows?.KbNumbers);
        List<PackageSummary> listed = await RegisteredMachine.ReadAsync<List<PackageSummary>>(await administrator.GetAsync(PackageRequests.Packages));
        Assert.Equal([5132061, 5043080], Assert.Single(listed, item => item.Id == package.Id).Windows?.KbNumbers);
    }

    [Fact]
    public async Task AZipOfLanguageCabinetsIsALanguage()
    {
        SignedInClient administrator = await application.AdministratorAsync();

        PackageSummary package = await administrator.UploadedPackageAsync(
            TestZip.Create(TestLanguageIso.GermanPack, TestLanguageIso.GermanBasic),
            UploadKind.WindowsPackage,
            "german.zip");

        Assert.Equal(WindowsPackageContent.Language, package.Windows?.Content);
        Assert.Equal(["de-DE"], package.Windows?.Languages);
    }

    [Fact]
    public async Task AWindowsPackageHasNoTargets()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        PackageSummary package = await administrator.UploadedPackageAsync(TestZip.Create(Update), UploadKind.WindowsPackage, "update.zip");

        HttpResponseMessage refused = await administrator.SendJsonAsync(
            HttpMethod.Put,
            $"{PackageRequests.Packages}/{package.Id}",
            """{"name":"Update","description":null,"targets":[{"manufacturer":null,"model":"Latitude*"}]}""");

        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        Assert.Contains("package.windowsNoTargets", await refused.Content.ReadAsStringAsync(TestContext.Current.CancellationToken), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("readme.txt", "package.windowsFile")]
    [InlineData("updates/update.msu", "package.windowsFolder")]
    public async Task AZipWithAnythingButMsuAndCabFilesAtItsRootIsRefused(string entry, string code)
    {
        SignedInClient administrator = await application.AdministratorAsync();

        HttpResponseMessage refused = await administrator.UploadPackageAsync(TestZip.Create(Update, entry), UploadKind.WindowsPackage, "mixed.zip");

        Assert.Equal(HttpStatusCode.UnprocessableEntity, refused.StatusCode);
        Assert.Equal(code, await CodeAsync(refused));
    }

    [Fact]
    public async Task AZipOfOnlyAFolderHoldsNoPackageFile()
    {
        SignedInClient administrator = await application.AdministratorAsync();

        HttpResponseMessage refused = await administrator.UploadPackageAsync(TestZip.Create("empty/"), UploadKind.WindowsPackage, "empty.zip");

        Assert.Equal("package.windowsEmpty", await CodeAsync(refused));
    }

    [Fact]
    public async Task AFileThatIsNoPackageIsRefused()
    {
        SignedInClient administrator = await application.AdministratorAsync();

        HttpResponseMessage refused = await administrator.UploadPackageAsync([1, 2, 3], UploadKind.WindowsPackage, "setup.exe");

        Assert.Equal("upload.windowsPackageFile", await CodeAsync(refused));
    }

    [Fact]
    public async Task AnUploadedLanguageIsoGoesIntoTheImportFolder()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        string folder = Directory.CreateDirectory(Path.Combine(application.StorePath, "made")).FullName;
        byte[] iso = await File.ReadAllBytesAsync(TestLanguageIso.Write(Path.Combine(folder, "lof.iso")), TestContext.Current.CancellationToken);
        ImageUploadSession session = await administrator.StartPackageUploadAsync(iso, UploadKind.WindowsPackage, "Languages.iso");
        (await administrator.SendChunkAsync(session.Id, 0, iso)).EnsureSuccessStatusCode();

        HttpResponseMessage completed = await administrator.CompleteUploadAsync(session.Id);

        Assert.Equal(HttpStatusCode.Created, completed.StatusCode);
        ImportFile placed = await RegisteredMachine.ReadAsync<ImportFile>(completed);
        Assert.Equal(application.Services.GetRequiredService<ImportFolders>().Default, Path.GetDirectoryName(placed.Path));
        Assert.Equal(iso, await File.ReadAllBytesAsync(placed.Path, TestContext.Current.CancellationToken));
        Assert.Empty(Directory.EnumerateFileSystemEntries(Store.UploadsDirectory, $"{session.Id:N}*"));

        // The answer was lost: the browser asks again and hears the same.
        Assert.Equal(placed, await RegisteredMachine.ReadAsync<ImportFile>(await administrator.CompleteUploadAsync(session.Id)));
    }

    [Fact]
    public async Task AnIsoWithoutTheLanguagesFolderIsRefused()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        string path = Path.Combine(Directory.CreateDirectory(Path.Combine(application.StorePath, "made")).FullName, "other.iso");
        DiscUtils.Iso9660.CDBuilder builder = new() { UseJoliet = true };
        builder.AddFile(@"sources\install.wim", [1, 2, 3]);
        builder.Build(path);

        HttpResponseMessage refused = await administrator.UploadPackageAsync(
            await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken),
            UploadKind.WindowsPackage,
            "Win11.iso");

        Assert.Equal("iso.noLanguages", await CodeAsync(refused));
    }
}
