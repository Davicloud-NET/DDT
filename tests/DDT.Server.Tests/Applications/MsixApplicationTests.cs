// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using DDT.Contracts.Applications;
using Xunit;
using static DDT.Server.Tests.ApplicationRequests;

namespace DDT.Server.Tests.Applications;

// MSIX packages are recognised at upload, and saved only when DISM could take them.
public sealed class MsixApplicationTests(DdtApplication application) : IClassFixture<DdtApplication>
{
    private const string Family = "Contoso.App_8wekyb3d8bbwe";

    private static SaveApplicationRequest Msix(string name, Guid packageId) =>
        new(0, name, null, null, null, ApplicationKind.Msix, packageId, null, [0], [3010], new PackageFamilyDetection(Family), [], null, null, 60, false);

    private static byte[] Zip(params (string Name, byte[] Content)[] entries)
    {
        using MemoryStream zip = new();

        using (ZipArchive archive = new(zip, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach ((string name, byte[] content) in entries)
            {
                using Stream data = archive.CreateEntry(name).Open();
                data.Write(content);
            }
        }

        return zip.ToArray();
    }

    [Fact]
    public async Task AnMsixFileSuggestsAnMsixApplicationDetectedByItsFamily()
    {
        SignedInClient administrator = await application.AdministratorAsync();

        ApplicationUpload upload = await administrator.UploadedApplicationAsync(TestMsix.Package("Contoso.App"), "Contoso.App_1.2.3.0_x64.msix");

        ApplicationSuggestion suggestion = Assert.IsType<ApplicationSuggestion>(upload.Suggestion);
        Assert.Equal(
            (ApplicationKind.Msix, "Contoso App", "Contoso", "1.2.3.0", (string?)null),
            (suggestion.Kind, suggestion.Name, suggestion.Publisher, suggestion.Version, suggestion.Command));
        Assert.Equal(new PackageFamilyDetection(Family), suggestion.Detection);
        Assert.Equal([0], suggestion.SuccessExitCodes);
        Assert.Equal([3010], suggestion.RestartExitCodes);
    }

    [Fact]
    public async Task AZipSuggestsItsMainPackageAndKeepsTheFrameworksAsItsDependencies()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        byte[] zip = Zip(
            ("Contoso.App.msixbundle", TestMsix.Bundle("Contoso.App")),
            ("Dependencies/x64/Microsoft.VCLibs.x64.appx", TestMsix.Package("Microsoft.VCLibs.140.00", framework: true)),
            ("setup.exe", RandomNumberGenerator.GetBytes(100)));

        ApplicationUpload upload = await administrator.UploadedApplicationAsync(zip, "contoso.zip");

        ApplicationSuggestion suggestion = Assert.IsType<ApplicationSuggestion>(upload.Suggestion);
        Assert.Equal(
            (ApplicationKind.Msix, "Contoso.App", (string?)null, "2.0.0.0", (string?)null),
            (suggestion.Kind, suggestion.Name, suggestion.Publisher, suggestion.Version, suggestion.Command));
        Assert.Equal(new PackageFamilyDetection(Family), suggestion.Detection);
        Assert.Equal(HttpStatusCode.Created, (await administrator.PostAsync(ApplicationsPath, Msix(Unique("Contoso"), upload.Files.PackageId))).StatusCode);
    }

    [Fact]
    public async Task AnInstallerStillSuggestsACommand()
    {
        SignedInClient administrator = await application.AdministratorAsync();

        ApplicationUpload upload = await administrator.UploadedApplicationAsync(TestMsi.Fixture(), $"setup-{Guid.NewGuid():N}.msi");

        Assert.Equal(ApplicationKind.Command, upload.Suggestion?.Kind);
    }

    [Fact]
    public async Task TwoMainPackagesSuggestNothingAndAreRefusedOnSave()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        byte[] zip = Zip(("a/Contoso.App.msix", TestMsix.Package("Contoso.App")), ("b/Fabrikam.Tool.msix", TestMsix.Package("Fabrikam.Tool")));
        ApplicationUpload upload = await administrator.UploadedApplicationAsync(zip, "two.zip");

        HttpResponseMessage refused = await administrator.PostAsync(ApplicationsPath, Msix(Unique("Two"), upload.Files.PackageId));

        Assert.Null(upload.Suggestion);
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        Assert.Equal(["application.msixTwoMains"], await ErrorCodesAsync(refused, "packageId"));
    }

    [Fact]
    public async Task FilesWithoutAPackageAreRefusedForMsixButNotForACommand()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        ApplicationUpload upload = await administrator.UploadedApplicationAsync(RandomNumberGenerator.GetBytes(64), "configure.ps1");

        HttpResponseMessage refused = await administrator.PostAsync(ApplicationsPath, Msix(Unique("Script"), upload.Files.PackageId));
        HttpResponseMessage command = await administrator.PostAsync(ApplicationsPath, Command(Unique("Script"), upload.Files.PackageId));

        Assert.Equal(["application.msixNone"], await ErrorCodesAsync(refused, "packageId"));
        Assert.Equal(HttpStatusCode.Created, command.StatusCode);
    }

    [Fact]
    public async Task AnUnreadablePackageIsNamedOnSave()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        ApplicationUpload upload = await administrator.UploadedApplicationAsync(RandomNumberGenerator.GetBytes(64), "Broken.msix");

        HttpResponseMessage refused = await administrator.PostAsync(ApplicationsPath, Msix(Unique("Broken"), upload.Files.PackageId));

        Assert.Null(upload.Suggestion);
        Assert.Equal(["application.msixUnreadable"], await ErrorCodesAsync(refused, "packageId"));
    }
}
