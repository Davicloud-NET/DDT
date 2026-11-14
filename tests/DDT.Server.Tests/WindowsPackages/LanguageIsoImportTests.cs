// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.IO.Compression;
using System.Net;
using DDT.Contracts.Import;
using DDT.Contracts.Packages;
using DDT.Server.Authentication;
using DDT.Server.Images;
using DDT.Server.Import;
using DiscUtils.Iso9660;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static DDT.Server.Tests.AccountRequests;

namespace DDT.Server.Tests.WindowsPackages;

// The languages of a Languages and Optional Features ISO in an import folder become Windows packages, one per language.
public sealed class LanguageIsoImportTests : IDisposable
{
    private const string Inspect = "/api/images/import/languages/inspect";
    private const string Import = "/api/images/import/languages";
    private const string IsoName = "26100.1.240331-1435.ge_release_amd64fre_CLIENT_LOF_PACKAGES_OEM.iso";

    private readonly ImageImportApplication _application = new();

    private string ImportFolder => Directory.CreateDirectory(_application.Services.GetRequiredService<ImportFolders>().Default).FullName;

    [Fact]
    public async Task TheIsoListsTheLanguagesOfItsLanguagePacks()
    {
        SignedInClient administrator = await _application.AdministratorAsync();
        string iso = TestLanguageIso.Write(Path.Combine(ImportFolder, IsoName));

        LanguageIsoView view = await RegisteredMachine.ReadAsync<LanguageIsoView>(await administrator.PostAsync(Inspect, new InspectLanguageIsoRequest(iso)));

        Assert.Equal(iso, view.Path);
        Assert.Equal(["de-DE", "ja-JP"], view.Languages);
        Assert.Equal("(26100)", view.Suffix);
    }

    [Fact]
    public async Task ALanguageBecomesAPackageWithItsFeaturesAndFonts()
    {
        SignedInClient administrator = await _application.AdministratorAsync();
        string iso = TestLanguageIso.Write(Path.Combine(ImportFolder, IsoName));

        Assert.Equal(HttpStatusCode.Accepted, (await administrator.PostAsync(Import, new ImportLanguagesRequest(iso, ["ja-jp", "xx-XX"], " (26100) "))).StatusCode);
        await _application.Services.GetRequiredService<LibraryImports>().Last;

        ImportResult result = Assert.Single((await SourcesAsync(administrator)).Job?.Results ?? []);
        Assert.Equal(("ja-JP language pack (26100)", ImportOutcome.Added), (result.Name, result.Outcome));
        PackageSummary package = Assert.Single(await PackagesAsync(administrator));
        Assert.Equal(("ja-JP language pack (26100)", PackageKind.WindowsPackage, $"From {IsoName}."), (package.Name, package.Kind, package.Description));
        Assert.Equal((WindowsPackageContent.Language, "x64"), (package.Windows?.Content, package.Windows?.Architecture));
        Assert.Equal(["ja-JP"], package.Windows?.Languages);
        Assert.Equal([26100], package.Windows?.Builds);

        using (ZipArchive zip = ZipFile.OpenRead(_application.Services.GetRequiredService<ImageStore>().ObjectPath(package.Sha256)))
        {
            Assert.Equal(
                [TestLanguageIso.JapanesePack, TestLanguageIso.JapaneseFonts, TestLanguageIso.JapaneseOcr],
                zip.Entries.Select(entry => entry.FullName).Order(StringComparer.Ordinal));
            ZipArchiveEntry pack = Assert.Single(zip.Entries, entry => entry.FullName == TestLanguageIso.JapanesePack);
            await using Stream data = pack.Open();
            using MemoryStream content = new();
            await data.CopyToAsync(content, TestContext.Current.CancellationToken);
            Assert.Equal(TestLanguageIso.Content(TestLanguageIso.JapanesePack), content.ToArray());
        }

        // The same language again gives the same zip, which the library has
        (await administrator.PostAsync(Import, new ImportLanguagesRequest(iso, ["ja-JP"], "(26100)"))).EnsureSuccessStatusCode();
        await _application.Services.GetRequiredService<LibraryImports>().Last;
        Assert.Equal(ImportOutcome.Existing, (await SourcesAsync(administrator)).Job?.Results[0].Outcome);
    }

    [Fact]
    public async Task OnlyAnAdministratorReadsAnIsoInAnImportFolder()
    {
        SignedInClient administrator = await _application.AdministratorAsync();
        using SignedInClient operatorClient = await _application.SignInAsync(DdtRoleNames.Operator);
        string iso = TestLanguageIso.Write(Path.Combine(ImportFolder, IsoName));
        string outside = TestLanguageIso.Write(Path.Combine(_application.StorePath, "outside.iso"));
        string notIso = Path.Combine(ImportFolder, "fake.iso");
        await File.WriteAllBytesAsync(notIso, new byte[40_000], TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, (await operatorClient.PostAsync(Inspect, new InspectLanguageIsoRequest(iso))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await operatorClient.PostAsync(Import, new ImportLanguagesRequest(iso, ["de-DE"]))).StatusCode);
        Assert.Equal("import.notAnIso", await CodeAsync(await administrator.PostAsync(Inspect, new InspectLanguageIsoRequest(outside))));
        Assert.Equal("import.notAnIso", await CodeAsync(await administrator.PostAsync(Inspect, new InspectLanguageIsoRequest(notIso))));
        Assert.Equal("import.notAnIso", await CodeAsync(await administrator.PostAsync(Import, new ImportLanguagesRequest(outside, ["de-DE"]))));
    }

    [Fact]
    public async Task AnIsoWithoutTheLanguagesFolderIsRefusedInOneSentence()
    {
        SignedInClient administrator = await _application.AdministratorAsync();
        string iso = Path.Combine(ImportFolder, "windows.iso");
        CDBuilder builder = new() { UseJoliet = true };
        builder.AddFile(@"sources\install.wim", [1, 2, 3]);
        builder.Build(iso);

        HttpResponseMessage refused = await administrator.PostAsync(Inspect, new InspectLanguageIsoRequest(iso));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, refused.StatusCode);
        Assert.Equal("iso.noLanguages", await CodeAsync(refused));
    }

    [Fact]
    public async Task AnImportNeedsALanguageAndAShortSuffix()
    {
        SignedInClient administrator = await _application.AdministratorAsync();
        string iso = TestLanguageIso.Write(Path.Combine(ImportFolder, IsoName));

        Assert.Equal("import.nothing", await CodeAsync(await administrator.PostAsync(Import, new ImportLanguagesRequest(iso, ["ca-ES"]))));
        Assert.Equal("import.suffix", await CodeAsync(await administrator.PostAsync(Import, new ImportLanguagesRequest(iso, ["de-DE"], new string('x', 65)))));
    }

    public void Dispose() => _application.Dispose();

    private static async Task<ImportSources> SourcesAsync(SignedInClient client) =>
        await RegisteredMachine.ReadAsync<ImportSources>(await client.GetAsync("/api/images/import"));

    private static async Task<List<PackageSummary>> PackagesAsync(SignedInClient client) =>
        await RegisteredMachine.ReadAsync<List<PackageSummary>>(await client.GetAsync(PackageRequests.Packages));
}
