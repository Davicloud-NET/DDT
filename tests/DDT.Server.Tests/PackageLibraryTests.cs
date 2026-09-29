// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Threading.Channels;
using DDT.Contracts.Deployments;
using DDT.Contracts.Images;
using DDT.Contracts.Machines;
using DDT.Contracts.Packages;
using DDT.Contracts.Sequences;
using DDT.Server.Authentication;
using DDT.Server.Data;
using DDT.Server.Images;
using DDT.Server.Live;
using DDT.Server.Machines;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DDT.Server.Tests;

public sealed class PackageLibraryTests(DdtApplication application) : IClassFixture<DdtApplication>
{
    private ImageStore Store => application.Services.GetRequiredService<ImageStore>();

    private static Task<T> ReadAsync<T>(HttpResponseMessage response) => RegisteredMachine.ReadAsync<T>(response);

    private async Task<IReadOnlyList<PackageSummary>> ListAsync() =>
        await ReadAsync<IReadOnlyList<PackageSummary>>(await (await application.AdministratorAsync()).GetAsync(PackageRequests.Packages));

    private Task<List<AuditEvent>> AuditAsync(Guid packageId)
    {
        string subject = packageId.ToString("D");

        return application.QueryAsync(database => database.AuditEvents
            .AsNoTracking()
            .Where(e => e.SubjectId == subject)
            .OrderBy(e => e.Id)
            .ToListAsync(TestContext.Current.CancellationToken));
    }

    private static async Task<IDictionary<string, string[]>> ProblemsAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        return (await response.Content.ReadFromJsonAsync<ValidationProblemDetails>(TestContext.Current.CancellationToken))!.Errors;
    }

    [Fact]
    public async Task AddsADriverPackageFromAZipWithoutUnpackingItOnDisk()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        using SignedInClient viewer = await application.SignInAsync(DdtRoleNames.Viewer);
        await using LiveListener live = await LiveListener.StartAsync(application, viewer);
        ChannelReader<PackageSummary> changes = live.Listen<PackageSummary>(LiveEvents.PackageChanged);
        byte[] zip = TestZip.Create(("Audio/HDX.inf", new byte[1000]), ("Audio/hdx.sys", RandomNumberGenerator.GetBytes(3000)));
        string fileName = $"latitude-{Guid.NewGuid():N}.zip";

        ImageUploadSession session = await administrator.StartPackageUploadAsync(zip, UploadKind.Drivers, fileName);
        Assert.Equal(UploadKind.Drivers, session.Kind);
        (await administrator.SendChunkAsync(session.Id, 0, zip)).EnsureSuccessStatusCode();

        HttpResponseMessage completed = await administrator.CompleteUploadAsync(session.Id);
        Assert.Equal(HttpStatusCode.Created, completed.StatusCode);
        PackageSummary package = await ReadAsync<PackageSummary>(completed);

        Assert.Equal(fileName[..^4], package.Name);
        Assert.Equal(PackageKind.Drivers, package.Kind);
        Assert.Equal(TestWim.Sha256(zip), package.Sha256);
        Assert.Equal(zip.Length, package.SizeBytes);
        Assert.Equal(4000, package.ExpandedBytes);
        Assert.Equal(2, package.FileCount);
        Assert.Empty(package.Targets);
        Assert.Equal(fileName, package.OriginalFileName);
        Assert.StartsWith("administrator-", package.UploadedBy, StringComparison.Ordinal);

        Assert.Equal(package.Id, Assert.Single(await ListAsync(), p => p.Id == package.Id).Id);
        Assert.Equal(zip, await File.ReadAllBytesAsync(Store.ObjectPath(package.Sha256), TestContext.Current.CancellationToken));
        Assert.Empty(Directory.EnumerateFileSystemEntries(Store.UploadsDirectory, $"{session.Id:N}*"));
        Assert.Equal(package with { Targets = [] }, (await LiveListener.NextAsync(changes, p => p.Id == package.Id)) with { Targets = [] });

        AuditEvent audit = Assert.Single(await AuditAsync(package.Id));
        Assert.Equal(AuditActions.PackageUploaded, audit.Action);
        Assert.Equal($"{package.Name}, Drivers, 2 files from {fileName}, SHA-256 {package.Sha256}.", audit.Detail);

        // The answer to the completion was lost, and the browser asks again.
        HttpResponseMessage again = await administrator.CompleteUploadAsync(session.Id);
        Assert.Equal(HttpStatusCode.OK, again.StatusCode);
        Assert.Equal(package.Id, (await ReadAsync<PackageSummary>(again)).Id);
    }

    [Fact]
    public async Task TheSameZipIsOnePackageOfEachKind()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        byte[] zip = PackageRequests.DriverZip();
        PackageSummary drivers = await administrator.UploadedPackageAsync(zip, UploadKind.Drivers);

        HttpResponseMessage sameKind = await administrator.UploadPackageAsync(zip, UploadKind.Drivers);
        Assert.Equal(HttpStatusCode.OK, sameKind.StatusCode);
        Assert.Equal(drivers.Id, (await ReadAsync<PackageSummary>(sameKind)).Id);

        HttpResponseMessage otherKind = await administrator.UploadPackageAsync(zip, UploadKind.Files);
        Assert.Equal(HttpStatusCode.Created, otherKind.StatusCode);
        PackageSummary files = await ReadAsync<PackageSummary>(otherKind);

        Assert.NotEqual(drivers.Id, files.Id);
        Assert.Equal(PackageKind.Files, files.Kind);
        Assert.Equal(drivers.Sha256, files.Sha256);
    }

    [Fact]
    public async Task TheSameFileSelectedAsAnotherKindIsAnotherUpload()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        byte[] zip = PackageRequests.DriverZip();
        string fileName = $"tools-{Guid.NewGuid():N}.zip";

        ImageUploadSession drivers = await administrator.StartPackageUploadAsync(zip, UploadKind.Drivers, fileName);
        ImageUploadSession files = await administrator.StartPackageUploadAsync(zip, UploadKind.Files, fileName);
        ImageUploadSession driversAgain = await administrator.StartPackageUploadAsync(zip, UploadKind.Drivers, fileName);

        Assert.NotEqual(drivers.Id, files.Id);
        Assert.Equal(drivers.Id, driversAgain.Id);
    }

    [Fact]
    public async Task RefusesAZipThatCannotBeAPackageAndSaysWhyAgain()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        byte[] zip = TestZip.Create("readme.txt");
        ImageUploadSession session = await administrator.StartPackageUploadAsync(zip, UploadKind.Drivers);
        (await administrator.SendChunkAsync(session.Id, 0, zip)).EnsureSuccessStatusCode();

        HttpResponseMessage refused = await administrator.CompleteUploadAsync(session.Id);
        HttpResponseMessage again = await administrator.CompleteUploadAsync(session.Id);

        const string reason = "A driver package needs at least one .inf file. Zip the folder that holds the drivers' .inf files.";
        Assert.Equal(HttpStatusCode.UnprocessableEntity, refused.StatusCode);
        Assert.Equal(reason, await TestDatabase.TitleAsync(refused));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, again.StatusCode);
        Assert.Equal(reason, await TestDatabase.TitleAsync(again));
        Assert.DoesNotContain(await ListAsync(), p => p.Sha256 == TestWim.Sha256(zip));
        Assert.False(File.Exists(Store.PartPath(session.Id)));
    }

    [Fact]
    public async Task SetsTheModelsADriverPackageIsFor()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        using SignedInClient viewer = await application.SignInAsync(DdtRoleNames.Viewer);
        await using LiveListener live = await LiveListener.StartAsync(application, viewer);
        ChannelReader<PackageSummary> changes = live.Listen<PackageSummary>(LiveEvents.PackageChanged);
        PackageSummary package = await administrator.UploadedPackageAsync(PackageRequests.DriverZip(), UploadKind.Drivers);
        await LiveListener.NextAsync(changes, p => p.Id == package.Id);

        HttpResponseMessage saved = await administrator.PutAsync(
            $"{PackageRequests.Packages}/{package.Id}",
            new UpdatePackageRequest(
                " Latitude drivers ",
                "From the vendor's pack.\0",
                [new HardwareModel("  Dell   Inc. ", " Latitude  5440 "), new HardwareModel(null, "Latitude 7*")]));
        PackageSummary updated = await ReadAsync<PackageSummary>(saved);

        Assert.Equal("Latitude drivers", updated.Name);
        Assert.Equal("From the vendor's pack.", updated.Description);
        Assert.Equal([new HardwareModel("Dell Inc.", "Latitude 5440"), new HardwareModel(null, "Latitude 7*")], updated.Targets.ToArray());
        Assert.Equal(updated.Targets, Assert.Single(await ListAsync(), p => p.Id == package.Id).Targets);

        // What the page patches its list with, targets and all.
        PackageSummary pushed = await LiveListener.NextAsync(changes, p => p.Id == package.Id);
        Assert.Equal(updated with { Targets = [] }, pushed with { Targets = [] });
        Assert.Equal(updated.Targets, pushed.Targets);

        AuditEvent audit = (await AuditAsync(package.Id))[^1];
        Assert.Equal(AuditActions.PackageChanged, audit.Action);
        Assert.Equal("Latitude drivers, for Dell Inc. Latitude 5440; any maker Latitude 7*.", audit.Detail);
    }

    [Fact]
    public async Task RefusesTargetsThatCannotMatchAMachine()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        PackageSummary drivers = await administrator.UploadedPackageAsync(PackageRequests.DriverZip(), UploadKind.Drivers);
        PackageSummary files = await administrator.UploadedPackageAsync(TestZip.Create("setup.cmd"), UploadKind.Files);

        HardwareModel[][] refused =
        [
            [new HardwareModel(null, "To Be Filled By O.E.M.")],
            [new HardwareModel("System manufacturer", "Latitude 5440")],
            [new HardwareModel("Dell*", "Latitude 5440")],
            [new HardwareModel(null, "La*")],
            [new HardwareModel(null, "Lat*itude")],
            [new HardwareModel(null, " ")],
            [new HardwareModel("Dell", new string('x', 129))],
            [new HardwareModel("Dell", "Latitude 5440"), new HardwareModel("DELL", " latitude  5440")],
            [.. Enumerable.Range(0, 65).Select(i => new HardwareModel(null, $"Model {i}"))],
        ];

        foreach (HardwareModel[] targets in refused)
        {
            HttpResponseMessage response = await administrator.PutAsync(
                $"{PackageRequests.Packages}/{drivers.Id}",
                new UpdatePackageRequest(drivers.Name, null, targets));

            Assert.Equal(["targets"], (await ProblemsAsync(response)).Keys);
        }

        HttpResponseMessage filesWithTargets = await administrator.PutAsync(
            $"{PackageRequests.Packages}/{files.Id}",
            new UpdatePackageRequest(files.Name, null, [new HardwareModel(null, "Latitude 5440")]));
        Assert.StartsWith("A Files package is unpacked for the Run script steps", (await ProblemsAsync(filesWithTargets))["targets"][0], StringComparison.Ordinal);

        HttpResponseMessage unnamed = await administrator.PutAsync($"{PackageRequests.Packages}/{drivers.Id}", new UpdatePackageRequest(" ", null, []));
        Assert.Equal(["name"], (await ProblemsAsync(unnamed)).Keys);

        Assert.Equal(
            HttpStatusCode.NotFound,
            (await administrator.PutAsync($"{PackageRequests.Packages}/{Guid.NewGuid()}", new UpdatePackageRequest("x", null, []))).StatusCode);
    }

    [Fact]
    public async Task OnlyAnAdministratorChangesPackages()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        using SignedInClient viewer = await application.SignInAsync(DdtRoleNames.Viewer);
        using SignedInClient operatorClient = await application.SignInAsync(DdtRoleNames.Operator);
        PackageSummary package = await administrator.UploadedPackageAsync(PackageRequests.DriverZip(), UploadKind.Drivers);

        Assert.Equal(HttpStatusCode.OK, (await viewer.GetAsync(PackageRequests.Packages)).StatusCode);
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await operatorClient.PutAsync($"{PackageRequests.Packages}/{package.Id}", new UpdatePackageRequest("x", null, []))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await operatorClient.DeleteAsync($"{PackageRequests.Packages}/{package.Id}")).StatusCode);
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await operatorClient.PostAsync(ImageUploadRequests.Uploads, new CreateImageUploadRequest("d.zip", 10, 1, UploadKind.Drivers))).StatusCode);
    }

    [Fact]
    public async Task DeletesAPackageAndItsFile()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        await using LiveListener live = await LiveListener.StartAsync(application, administrator);
        ChannelReader<PackagesRemovedEvent> removals = live.Listen<PackagesRemovedEvent>(LiveEvents.PackagesRemoved);
        PackageSummary package = await administrator.UploadedPackageAsync(PackageRequests.DriverZip(), UploadKind.Drivers);

        Assert.Equal(HttpStatusCode.NoContent, (await administrator.DeleteAsync($"{PackageRequests.Packages}/{package.Id}")).StatusCode);
        Assert.Equal([package.Id], (await LiveListener.NextAsync(removals)).PackageIds);

        Assert.DoesNotContain(await ListAsync(), p => p.Id == package.Id);
        Assert.False(File.Exists(Store.ObjectPath(package.Sha256)));
        Assert.Equal(AuditActions.PackageDeleted, (await AuditAsync(package.Id))[^1].Action);
        Assert.Equal(HttpStatusCode.NotFound, (await administrator.DeleteAsync($"{PackageRequests.Packages}/{package.Id}")).StatusCode);
    }

    // The file goes only when no package, image or waiting run needs it any more.
    [Fact]
    public async Task KeepsAPackageAndItsFileWhileARunNeedsThem()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        byte[] zip = PackageRequests.DriverZip();
        PackageSummary drivers = await administrator.UploadedPackageAsync(zip, UploadKind.Drivers);
        PackageSummary files = await administrator.UploadedPackageAsync(zip, UploadKind.Files);
        string stored = Store.ObjectPath(drivers.Sha256);

        Guid run = await application.AddAssignedRunAsync(ArtifactKind.Drivers, drivers.Id, drivers.Sha256);

        HttpResponseMessage inUse = await administrator.DeleteAsync($"{PackageRequests.Packages}/{drivers.Id}");
        Assert.Equal(HttpStatusCode.Conflict, inUse.StatusCode);
        Assert.StartsWith("Machines are waiting to install this package", await TestDatabase.TitleAsync(inUse), StringComparison.Ordinal);

        // The files package of the same zip is deleted, but the run still downloads the file.
        Assert.Equal(HttpStatusCode.NoContent, (await administrator.DeleteAsync($"{PackageRequests.Packages}/{files.Id}")).StatusCode);
        Assert.True(File.Exists(stored));

        await application.EndRunAsync(run);
        Assert.Equal(HttpStatusCode.NoContent, (await administrator.DeleteAsync($"{PackageRequests.Packages}/{drivers.Id}")).StatusCode);
        Assert.False(File.Exists(stored));
    }

    [Fact]
    public async Task KeepsAnImageAndItsFileWhileARunNeedsThem()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        Image applied = await application.SeedImageAsync(RandomNumberGenerator.GetBytes(4096));
        Image copied = await application.SeedImageAsync(RandomNumberGenerator.GetBytes(4096));

        Guid applying = await application.AddAssignedRunAsync(ArtifactKind.Image, applied.Id, applied.Sha256);
        Assert.Equal(HttpStatusCode.Conflict, (await administrator.DeleteAsync($"/api/images/{applied.Id}")).StatusCode);
        await application.EndRunAsync(applying);
        Assert.Equal(HttpStatusCode.NoContent, (await administrator.DeleteAsync($"/api/images/{applied.Id}")).StatusCode);

        // A run whose artifact came from another entry of the same file keeps the file, not the entry.
        await application.AddAssignedRunAsync(ArtifactKind.Image, Guid.NewGuid(), copied.Sha256);
        Assert.Equal(HttpStatusCode.NoContent, (await administrator.DeleteAsync($"/api/images/{copied.Id}")).StatusCode);
        Assert.True(File.Exists(Store.ObjectPath(copied.Sha256)));
    }

    [Fact]
    public async Task AScriptRunsWithAFilesPackage()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        Image image = await application.SeedImageAsync(RandomNumberGenerator.GetBytes(4096));
        PackageSummary drivers = await administrator.UploadedPackageAsync(PackageRequests.DriverZip(), UploadKind.Drivers);
        PackageSummary files = await administrator.UploadedPackageAsync(TestZip.Create("setup.cmd"), UploadKind.Files);

        async Task<IReadOnlyList<SequenceProblem>> ProblemsWithAsync(Guid packageId)
        {
            SequenceDefinition definition = SequenceRequests.Minimal(image.Id);
            RunScriptStep script = new() { Id = Guid.NewGuid(), Name = "Setup", Script = "setup.cmd", PackageId = packageId };

            return (await ReadAsync<SequenceValidation>(await administrator.PostAsync(
                $"{SequenceRequests.Sequences}/validate",
                definition with { Steps = [.. definition.Steps, script] }))).Problems;
        }

        Assert.Empty(await ProblemsWithAsync(files.Id));
        Assert.Equal(
            $"{drivers.Name} is a driver package. A script runs with a files package.",
            Assert.Single(await ProblemsWithAsync(drivers.Id)).Message);
        Assert.Equal(
            "The package is no longer in the library. Choose another package, or none.",
            Assert.Single(await ProblemsWithAsync(Guid.NewGuid())).Message);
    }
}
