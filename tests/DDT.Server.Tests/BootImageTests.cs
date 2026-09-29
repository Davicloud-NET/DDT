// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Channels;
using DDT.Contracts.BootImage;
using DDT.Contracts.Packages;
using DDT.Server.Authentication;
using DDT.Server.BootImage;
using DDT.Server.Images;
using DDT.Server.Live;
using DDT.Server.Machines;
using DDT.Server.Packages;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DDT.Server.Tests;

// Driver flags apply to the whole library.
// So each test starts with no drivers flagged and no build in the boot directory.
public sealed class BootImageTests(DdtApplication application) : IClassFixture<DdtApplication>, IAsyncLifetime
{
    private const string BootImage = "/api/boot-image";

    private string ManifestPath => application.Services.GetRequiredService<BootImageCatalog>().ManifestPath;

    public async ValueTask InitializeAsync()
    {
        await application.QueryAsync(database => database.Packages.ExecuteUpdateAsync(
            update => update.SetProperty(p => p.BootImage, false),
            TestContext.Current.CancellationToken));

        if (File.Exists(ManifestPath))
        {
            File.Delete(ManifestPath);
        }
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private async Task<BootImageView> ViewAsync() =>
        await RegisteredMachine.ReadAsync<BootImageView>(await (await application.AdministratorAsync()).GetAsync(BootImage));

    private async Task<HttpResponseMessage> FlagAsync(Package package, bool? bootImage) =>
        await (await application.AdministratorAsync()).PutAsync(
            $"{PackageRequests.Packages}/{package.Id}",
            new UpdatePackageRequest(package.Name, null, [], bootImage));

    private async Task<PackageSummary> FlaggedAsync(Package package, bool? bootImage = true) =>
        await RegisteredMachine.ReadAsync<PackageSummary>(await FlagAsync(package, bootImage));

    // Writes the manifest the way Build-BootImage.ps1 does, with the drivers it put in.
    private async Task WriteManifestAsync(params Package[] drivers)
    {
        string entries = string.Join(",", drivers.Select(d => $$"""{ "packageId": "{{d.Id}}", "name": "{{d.Name}}", "sha256": "{{d.Sha256.ToUpperInvariant()}}" }"""));

        await WriteManifestAsync($$"""
            {
              "builtUtc": "2026-09-27T08:15:00.0000000Z",
              "driverSetHash": "whatever the script wrote",
              "drivers": [ {{entries}} ],
              "adkVersion": "10.1.26100.2454",
              "bootManager": "10.0.26100.2454",
              "agentVersion": "0.7.0"
            }
            """);
    }

    private async Task WriteManifestAsync(string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(ManifestPath)!);
        await File.WriteAllTextAsync(ManifestPath, content, TestContext.Current.CancellationToken);
    }

    // The definition the script and the page rely on: SHA-256 over "{id} {sha256}\n" per package, in the order of the ids.
    private static string ExpectedHash(params Package[] drivers) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(string.Concat(drivers
            .OrderBy(d => d.Id.ToString("D"), StringComparer.Ordinal)
            .Select(d => $"{d.Id:D} {d.Sha256}\n")))));

    [Fact]
    public async Task OnlyADriverPackageGoesIntoTheBootImage()
    {
        Package drivers = await application.SeedPackageAsync(PackageKind.Drivers);
        Package files = await application.SeedPackageAsync(PackageKind.Files);

        Assert.True((await FlaggedAsync(drivers)).BootImage);
        Assert.True((await FlaggedAsync(drivers, null)).BootImage);

        HttpResponseMessage refused = await FlagAsync(files, true);
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        Assert.Contains("bootImage", (await refused.Content.ReadFromJsonAsync<ValidationProblemDetails>(TestContext.Current.CancellationToken))!.Errors.Keys);
        Assert.False((await FlaggedAsync(files, false)).BootImage);

        Assert.False((await FlaggedAsync(drivers, false)).BootImage);

        List<string?> details = await application.QueryAsync(database => database.AuditEvents
            .AsNoTracking()
            .Where(e => e.SubjectId == drivers.Id.ToString("D") && e.Action == AuditActions.PackageChanged)
            .OrderBy(e => e.Id)
            .Select(e => e.Detail)
            .ToListAsync(TestContext.Current.CancellationToken));

        Assert.Equal(
            [
                $"{drivers.Name}, no targets. Added to the Windows PE boot image.",
                $"{drivers.Name}, no targets.",
                $"{drivers.Name}, no targets. Taken out of the Windows PE boot image.",
            ],
            details);
    }

    [Fact]
    public async Task WithoutFlaggedDriversOrABuildThereIsNothingToDo()
    {
        BootImageView view = await ViewAsync();

        Assert.Empty(view.Drivers);
        Assert.Null(view.DriverSetHash);
        Assert.Null(view.Build);
        Assert.False(view.Stale);
    }

    [Fact]
    public async Task ComparesTheFlaggedDriversWithTheBuild()
    {
        Package first = await application.SeedPackageAsync(PackageKind.Drivers);
        Package second = await application.SeedPackageAsync(PackageKind.Drivers);
        Package third = await application.SeedPackageAsync(PackageKind.Drivers);
        await FlaggedAsync(first);
        await FlaggedAsync(second);

        // Flagged but never built.
        BootImageView unbuilt = await ViewAsync();
        Assert.Equal(new[] { first, second }.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase).Select(p => p.Id), unbuilt.Drivers.Select(d => d.PackageId));
        Assert.Equal(new BootImageDriver(first.Id, first.Name, first.Sha256, first.SizeBytes), Assert.Single(unbuilt.Drivers, d => d.PackageId == first.Id));
        Assert.Equal(ExpectedHash(first, second), unbuilt.DriverSetHash);
        Assert.Null(unbuilt.Build);
        Assert.True(unbuilt.Stale);

        // Built with both, listed in another order and with the hash in upper case.
        await WriteManifestAsync(second, first);
        BootImageView built = await ViewAsync();
        Assert.False(built.Stale);
        Assert.NotNull(built.Build);
        Assert.Equal(new DateTimeOffset(2026, 9, 27, 8, 15, 0, TimeSpan.Zero), built.Build.BuiltUtc);
        Assert.Equal(built.DriverSetHash, built.Build.DriverSetHash);
        Assert.Equal([second.Id, first.Id], built.Build.Drivers.Select(d => d.PackageId));
        Assert.Equal(first.Sha256, Assert.Single(built.Build.Drivers, d => d.PackageId == first.Id).Sha256);
        Assert.Equal("10.1.26100.2454", built.Build.AdkVersion);
        Assert.Equal("10.0.26100.2454", built.Build.BootManager);
        Assert.Equal("0.7.0", built.Build.AgentVersion);

        await FlaggedAsync(third);
        Assert.True((await ViewAsync()).Stale);

        await FlaggedAsync(third, false);
        Assert.False((await ViewAsync()).Stale);

        await FlaggedAsync(first, false);
        await FlaggedAsync(second, false);
        BootImageView nothingFlagged = await ViewAsync();
        Assert.Null(nothingFlagged.DriverSetHash);
        Assert.True(nothingFlagged.Stale);

        // A build without DDT's drivers matches a library without flagged ones.
        await WriteManifestAsync();
        Assert.False((await ViewAsync()).Stale);
    }

    [Theory]
    [InlineData("not json at all")]
    [InlineData("""{ "drivers": [] }""")]
    [InlineData("""{ "builtUtc": "yesterday", "drivers": [] }""")]
    [InlineData("""{ "builtUtc": "2026-09-27T08:15:00Z", "drivers": [ { "packageId": "01a0e016-83f8-7ed8-ac65-1de28a3c3a4d", "sha256": "abc" } ] }""")]
    [InlineData("""{ "builtUtc": "2026-09-27T08:15:00Z", "drivers": [ { "name": "no id" } ] }""")]
    [InlineData("""{ "builtUtc": "2026-09-27T08:15:00Z", "drivers": [ null ] }""")]
    public async Task ABrokenDescriptionOfTheBuildCountsAsNoBuild(string content)
    {
        Package flagged = await application.SeedPackageAsync(PackageKind.Drivers);
        await FlaggedAsync(flagged);
        await WriteManifestAsync(content);

        BootImageView view = await ViewAsync();

        Assert.Null(view.Build);
        Assert.True(view.Stale);
    }

    // With comments and names in another case, as a person editing the file might leave it.
    [Fact]
    public async Task ReadsADescriptionAPersonEdited()
    {
        await WriteManifestAsync("""
            {
              // Rebuilt by hand after the NIC driver update.
              "BuiltUtc": "2026-09-27T10:00:00+02:00",
              "Drivers": [],
            }
            """);

        BootImageBuild? build = (await ViewAsync()).Build;

        Assert.NotNull(build);
        Assert.Equal(new DateTimeOffset(2026, 9, 27, 8, 0, 0, TimeSpan.Zero), build.BuiltUtc);
        Assert.Empty(build.Drivers);
        Assert.Null(build.DriverSetHash);
        Assert.Null(build.AgentVersion);
    }

    [Fact]
    public async Task AnAdministratorsTokenDownloadsAFlaggedDriver()
    {
        string administrator = await application.CreateUserAsync(DdtRoleNames.Administrator);
        string operatorName = await application.CreateUserAsync(DdtRoleNames.Operator);
        (_, string secret) = await application.SeedTokenAsync(administrator, DdtRoleNames.Administrator);
        (_, string operatorSecret) = await application.SeedTokenAsync(operatorName, DdtRoleNames.Operator);
        using HttpClient script = application.TokenClient(secret);
        using HttpClient operatorClient = application.TokenClient(operatorSecret);
        Package flagged = await application.SeedPackageAsync(PackageKind.Drivers);
        Package unflagged = await application.SeedPackageAsync(PackageKind.Drivers);
        await FlaggedAsync(flagged);
        byte[] stored = await File.ReadAllBytesAsync(application.Services.GetRequiredService<ImageStore>().ObjectPath(flagged.Sha256), TestContext.Current.CancellationToken);

        HttpResponseMessage whole = await script.GetPathAsync($"{BootImage}/drivers/{flagged.Id}/content");
        Assert.Equal(HttpStatusCode.OK, whole.StatusCode);
        Assert.Equal("application/zip", whole.Content.Headers.ContentType?.MediaType);
        Assert.Equal($"\"{flagged.Sha256}\"", whole.Headers.ETag?.Tag);
        Assert.Equal(stored, await whole.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken));

        using HttpRequestMessage ranged = new(HttpMethod.Get, new Uri($"{BootImage}/drivers/{flagged.Id}/content", UriKind.Relative));
        ranged.Headers.Range = new RangeHeaderValue(10, 19);
        HttpResponseMessage part = await script.SendAsync(ranged, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.PartialContent, part.StatusCode);
        Assert.Equal(stored[10..20], await part.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken));

        Assert.Equal(HttpStatusCode.NotFound, (await script.GetPathAsync($"{BootImage}/drivers/{unflagged.Id}/content")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await operatorClient.GetPathAsync($"{BootImage}/drivers/{flagged.Id}/content")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await operatorClient.GetPathAsync(BootImage)).StatusCode);
    }

    [Fact]
    public async Task PushesTheBootImageWhenItsDriversChange()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        await using LiveListener live = await LiveListener.StartAsync(application, administrator);
        ChannelReader<BootImageView> pushed = live.Listen<BootImageView>(LiveEvents.BootImageChanged);
        Package package = await application.SeedPackageAsync(PackageKind.Drivers);

        await FlaggedAsync(package);
        BootImageView flagged = await LiveListener.NextAsync(pushed, v => v.Drivers.Any(d => d.PackageId == package.Id));
        Assert.True(flagged.Stale);

        Assert.Equal(HttpStatusCode.NoContent, (await administrator.DeleteAsync($"{PackageRequests.Packages}/{package.Id}")).StatusCode);
        BootImageView deleted = await LiveListener.NextAsync(pushed);
        Assert.DoesNotContain(deleted.Drivers, d => d.PackageId == package.Id);
    }
}
