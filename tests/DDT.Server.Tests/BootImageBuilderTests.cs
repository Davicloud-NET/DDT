// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DDT.Contracts.BootImage;
using DDT.Pxe;
using DDT.Server.Authentication;
using DDT.Server.BootImage;
using DDT.Server.Images;
using DDT.Server.Machines;
using DDT.Server.Settings;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static DDT.Server.Tests.AccountRequests;

namespace DDT.Server.Tests;

// The builder for another PC, and the boot image it uploads.
public sealed class BootImageBuilderTests : IDisposable
{
    private const string Builder = "/api/boot-image/builder";
    private const string Upload = "/api/boot-image";

    private readonly BootImageBuildApplication _application = new();

    [Fact]
    public async Task AnAdministratorGetsABuilderAfterTheirPasswordWithWhatTheServerCameWith()
    {
        SignedInClient administrator = await _application.AdministratorAsync();
        using SignedInClient viewer = await _application.SignInAsync(DdtRoleNames.Operator);

        using HttpResponseMessage none = await DownloadAsync(administrator, await administrator.TokenAsync());
        Assert.Equal("bootImage.noBuilder", await CodeAsync(none));
        Assert.False((await ViewAsync(administrator)).Builder.Package);

        WriteRelease();
        Assert.True((await ViewAsync(administrator)).Builder.Package);
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.PostAsync(Builder)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await administrator.PostAsync(Builder)).StatusCode);

        using HttpResponseMessage download = await DownloadAsync(administrator, await administrator.TokenAsync());
        Assert.Equal(HttpStatusCode.OK, download.StatusCode);
        Assert.Equal("application/zip", download.Content.Headers.ContentType?.MediaType);

        using ZipArchive zip = new(await download.Content.ReadAsStreamAsync(TestContext.Current.CancellationToken));
        Assert.Equal(
            ["BootImage/DdtBootImage.psm1", "BootImage/Private/Image.ps1", "Build-BootImage.ps1", "Build.cmd", "boot-image-trim.txt", "builder.json", "ddt-agent.exe", "ddt-console.zip", "ddt-root.pem"],
            zip.Entries.Select(entry => entry.FullName).Order(StringComparer.Ordinal));
        Assert.Contains("-----BEGIN CERTIFICATE-----", Text(zip, "ddt-root.pem"), StringComparison.Ordinal);
        Assert.Contains("Build-BootImage.ps1", Text(zip, "Build.cmd"), StringComparison.Ordinal);

        BuilderFile? file = JsonSerializer.Deserialize(Text(zip, BuilderPackage.FileName), BuilderJsonContext.Default.BuilderFile);
        Assert.Equal((await ViewAsync(administrator)).Builder.ServerUrl, file?.ServerUrl);
        Assert.NotNull(_application.Services.GetRequiredService<BuilderTokens>().Validate(file?.UploadToken));
        Assert.Equal(1, await _application.QueryAsync(database =>
            database.AuditEvents.CountAsync(audit => audit.Action == AuditActions.BootImageBuilderDownloaded, TestContext.Current.CancellationToken)));
    }

    [Fact]
    public async Task TheBuilderCarriesTheFlaggedDriversUnpacked()
    {
        WriteRelease();
        ImageStore store = _application.Services.GetRequiredService<ImageStore>();
        byte[] package = Zip(("net/e1d.inf", "[Version]"), ("../outside.inf", "no"));
        string sha256 = Convert.ToHexStringLower(SHA256.HashData(package));
        Directory.CreateDirectory(store.ObjectsDirectory);
        await File.WriteAllBytesAsync(store.ObjectPath(sha256), package, TestContext.Current.CancellationToken);
        HelperDriver driver = new(Guid.NewGuid(), "Network drivers", sha256);

        await using FileStream built = await _application.Services.GetRequiredService<BuilderPackage>().CreateAsync(
            new BuilderFile("https://deploy01:8443", "token", DateTimeOffset.UnixEpoch),
            "-----BEGIN CERTIFICATE-----",
            new HelperDriverList(new string('a', 64), [driver]),
            TestContext.Current.CancellationToken);
        using ZipArchive zip = new(built, ZipArchiveMode.Read, leaveOpen: true);

        Assert.Equal(
            [$"drivers/{driver.PackageId:D}/net/e1d.inf", "drivers/drivers.json"],
            zip.Entries.Select(entry => entry.FullName).Where(name => name.StartsWith("drivers/", StringComparison.Ordinal)).Order(StringComparer.Ordinal));
        Assert.Contains(sha256, Text(zip, "drivers/drivers.json"), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ABuilderUploadsOneBootImageWhichTheServerChecksAndThenServes()
    {
        SignedInClient administrator = await _application.AdministratorAsync();
        using HttpClient builder = _application.CreateClient();
        (string token, _) = _application.Services.GetRequiredService<BuilderTokens>().Issue("admin");

        Assert.Equal(HttpStatusCode.NoContent, (await CheckAsync(builder, token)).StatusCode);
        Assert.Equal("bootImage.uploadToken", await CodeAsync(await UploadAsync(builder, "not a token", Build())));

        using HttpResponseMessage stray = await UploadAsync(builder, token, Build(("Windows/System32/evil.dll", "x")));
        Assert.Equal((HttpStatusCode.BadRequest, "bootImage.uploadStrayFile"), (stray.StatusCode, await CodeAsync(stray)));
        Assert.Equal("bootImage.uploadStrayFile", await CodeAsync(await UploadAsync(builder, token, Build(("Boot/../../evil", "x")))));
        Assert.Equal("bootImage.uploadMissingFile", await CodeAsync(await UploadAsync(builder, token, Zip(("Boot/BCD", "bcd")))));
        Assert.Equal("bootImage.uploadBroken", await CodeAsync(await UploadAsync(builder, token, Build(("Boot/boot.wim", "not a wim")))));
        Assert.Equal("bootImage.uploadBroken", await CodeAsync(await UploadAsync(builder, token, Encoding.UTF8.GetBytes("not a zip"))));
        Assert.Empty(BootBuilds.List(Catalog.BootDirectory));
        Assert.Equal(BootImageJobState.Failed, (await ViewAsync(administrator)).Job?.State);

        Assert.Equal(HttpStatusCode.NoContent, (await UploadAsync(builder, token, Build())).StatusCode);

        BootImageView view = await ViewAsync(administrator);
        BootImageStoredBuild served = Assert.Single(view.Builds);
        Assert.True(served.Current);
        Assert.Equal(DateTimeOffset.Parse("2026-10-02T08:00:00Z", System.Globalization.CultureInfo.InvariantCulture), view.Build?.BuiltUtc);
        Assert.Equal((BootImageJobKind.Upload, BootImageJobState.Succeeded, "admin"), (view.Job?.Kind, view.Job?.State, view.Job?.StartedBy));
        Assert.Equal("MSWIM\0\0\0 image", File.ReadAllText(Path.Combine(BootBuilds.Serving(Catalog.BootDirectory), "Boot", "boot.wim")));
        Assert.Empty(Directory.EnumerateDirectories(Path.Combine(Catalog.BootDirectory, BootBuilds.FolderName), ".upload-*"));

        // The token has done what it was for
        Assert.Equal(HttpStatusCode.Unauthorized, (await CheckAsync(builder, token)).StatusCode);
        Assert.Equal("bootImage.uploadToken", await CodeAsync(await UploadAsync(builder, token, Build())));
        Assert.Equal(1, await _application.QueryAsync(database =>
            database.AuditEvents.CountAsync(audit => audit.Action == AuditActions.BootImageUploaded && audit.ActorName == "admin (builder)", TestContext.Current.CancellationToken)));
    }

    public void Dispose() => _application.Dispose();

    private BootImageCatalog Catalog => _application.Services.GetRequiredService<BootImageCatalog>();

    private static async Task<BootImageView> ViewAsync(SignedInClient client) =>
        await RegisteredMachine.ReadAsync<BootImageView>(await client.GetAsync("/api/boot-image"));

    private static async Task<HttpResponseMessage> DownloadAsync(SignedInClient client, string proof)
    {
        using HttpRequestMessage request = new(HttpMethod.Post, new Uri(Builder, UriKind.Relative));
        request.Headers.Add(ReauthenticationTokens.HeaderName, proof);

        return await client.SendAsync(request, TestContext.Current.CancellationToken);
    }

    private static async Task<HttpResponseMessage> CheckAsync(HttpClient builder, string token)
    {
        using HttpRequestMessage request = new(HttpMethod.Get, new Uri(Builder, UriKind.Relative));
        request.Headers.Add(BuilderTokens.HeaderName, token);

        return await builder.SendAsync(request, TestContext.Current.CancellationToken);
    }

    private static async Task<HttpResponseMessage> UploadAsync(HttpClient builder, string token, byte[] zip)
    {
        using HttpRequestMessage request = new(HttpMethod.Put, new Uri(Upload, UriKind.Relative)) { Content = new ByteArrayContent(zip) };
        request.Headers.Add(BuilderTokens.HeaderName, token);

        return await builder.SendAsync(request, TestContext.Current.CancellationToken);
    }

    // What a release puts next to the server
    private void WriteRelease()
    {
        BundledReleases bundled = _application.Bundled;
        Directory.CreateDirectory(Path.Combine(bundled.Folder, "BootImage", "Private"));
        File.WriteAllText(Path.Combine(bundled.Folder, BuilderPackage.ScriptName), "# the script");
        File.WriteAllText(Path.Combine(bundled.Folder, "boot-image-trim.txt"), "# the list");
        File.WriteAllText(Path.Combine(bundled.Folder, "BootImage", "DdtBootImage.psm1"), "# the module");
        File.WriteAllText(Path.Combine(bundled.Folder, "BootImage", "Private", "Image.ps1"), "# a part of it");
        File.WriteAllBytes(bundled.AgentPath, Executables.Versioned("26.1.5"));
        File.WriteAllBytes(bundled.ConsolePath, Zip(("ddt-console.exe", "MZ")));
    }

    // A build as the builder zips it, with files of a test's own added or put in place of its.
    private static byte[] Build(params (string Name, string Text)[] others)
    {
        (string Name, string Text)[] build =
        [
            ("Boot/boot.wim", "MSWIM\0\0\0 image"),
            ("Boot/BCD", "bcd"),
            ("Boot/boot.sdi", "sdi"),
            ("Boot/ddt-boot-image.json", """{ "builtUtc": "2026-10-02T08:00:00Z", "drivers": [] }"""),
            ("EFI/Microsoft/Boot/boot.stl", "stl"),
            ("x64/bootmgfw.efi", "boot manager"),
        ];

        return Zip([.. build.Where(file => !others.Any(other => other.Name == file.Name)), .. others]);
    }

    private static byte[] Zip(params (string Name, string Text)[] files)
    {
        using MemoryStream zip = new();

        using (ZipArchive archive = new(zip, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach ((string name, string text) in files)
            {
                using StreamWriter entry = new(archive.CreateEntry(name).Open());
                entry.Write(text);
            }
        }

        return zip.ToArray();
    }

    private static string Text(ZipArchive zip, string name)
    {
        using StreamReader reader = new((zip.GetEntry(name) ?? throw new InvalidOperationException($"The zip has no {name}.")).Open());

        return reader.ReadToEnd();
    }
}
