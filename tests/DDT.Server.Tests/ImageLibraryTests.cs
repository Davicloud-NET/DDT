// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Net;
using System.Security.Cryptography;
using System.Threading.Channels;
using DDT.Contracts.Deployments;
using DDT.Contracts.Images;
using DDT.Server.Authentication;
using DDT.Server.Data;
using DDT.Server.Deployments;
using DDT.Server.Images;
using DDT.Server.Live;
using DDT.Server.Machines;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DDT.Server.Tests;

public sealed class ImageLibraryTests(DdtApplication application) : IClassFixture<DdtApplication>
{
    private const string Images = "/api/images";

    private ImageStore Store => application.Services.GetRequiredService<ImageStore>();

    private async Task SetStateAsync(Guid deploymentId, DeploymentState state)
    {
        using IServiceScope scope = application.Services.CreateScope();
        DdtDbContext database = scope.ServiceProvider.GetRequiredService<DdtDbContext>();
        Deployment deployment = await database.Deployments.SingleAsync(d => d.Id == deploymentId, TestContext.Current.CancellationToken);

        deployment.State = state;
        await database.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task ListsTheLibraryByNameForViewers()
    {
        using SignedInClient viewer = await application.SignInAsync(DdtRoleNames.Viewer);

        // The expected order differs from the insertion order, from the ordinal order ("B" before "a") and from the
        // order without the index ("a image" before "A image").
        Image c = await application.SeedImageAsync(RandomNumberGenerator.GetBytes(64), name: "c image");
        Image upperB = await application.SeedImageAsync(RandomNumberGenerator.GetBytes(64), architecture: "arm64", name: "B image");
        Image lowerA = await application.SeedImageAsync(RandomNumberGenerator.GetBytes(64), wimIndex: 2, name: "a image");
        Image upperA = await application.SeedImageAsync(RandomNumberGenerator.GetBytes(64), name: "A image");
        Guid[] seeded = [c.Id, upperB.Id, lowerA.Id, upperA.Id];

        IReadOnlyList<ImageSummary> library = await RegisteredMachine.ReadAsync<IReadOnlyList<ImageSummary>>(await viewer.GetAsync(Images));

        Assert.Equal([upperA.Id, lowerA.Id, upperB.Id, c.Id], library.Where(i => seeded.Contains(i.Id)).Select(i => i.Id));
        Assert.Equal(ImageSummaries.From(c), Assert.Single(library, i => i.Id == c.Id));
        Assert.Equal("arm64", Assert.Single(library, i => i.Id == upperB.Id).Architecture);

        using HttpClient anonymous = application.CreateClient();
        HttpResponseMessage listed = await anonymous.GetAsync(new Uri(Images, UriKind.Relative), TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Unauthorized, listed.StatusCode);
    }

    [Fact]
    public async Task OnlyAnAdministratorDeletesAnImage()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        SignedInClient administrator = await application.AdministratorAsync();
        using SignedInClient operatorClient = await application.SignInAsync(DdtRoleNames.Operator);
        Image image = await application.SeedImageAsync(RandomNumberGenerator.GetBytes(64));

        Assert.Equal(HttpStatusCode.Forbidden, (await operatorClient.DeleteAsync($"{Images}/{image.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await administrator.DeleteAsync($"{Images}/{image.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await administrator.DeleteAsync($"{Images}/{image.Id}")).StatusCode);

        IReadOnlyList<ImageSummary> library = await RegisteredMachine.ReadAsync<IReadOnlyList<ImageSummary>>(await administrator.GetAsync(Images));
        Assert.DoesNotContain(library, i => i.Id == image.Id);
        Assert.False(File.Exists(Store.ObjectPath(image.Sha256)));

        using IServiceScope scope = application.Services.CreateScope();
        DdtDbContext database = scope.ServiceProvider.GetRequiredService<DdtDbContext>();
        string subject = image.Id.ToString("D");

        Assert.True(await database.AuditEvents.AnyAsync(e => e.Action == AuditActions.ImageDeleted && e.SubjectId == subject, cancellationToken));
    }

    [Fact]
    public async Task DeletesARawDiskImageWithItsStoredDisk()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        SignedInClient administrator = await application.AdministratorAsync();
        Image image = await application.SeedRawImageAsync(RandomNumberGenerator.GetBytes(64), name: "noble");

        Assert.Equal(HttpStatusCode.NoContent, (await administrator.DeleteAsync($"{Images}/{image.Id}")).StatusCode);
        Assert.False(File.Exists(Store.ObjectPath(image.Sha256)));

        using IServiceScope scope = application.Services.CreateScope();
        DdtDbContext database = scope.ServiceProvider.GetRequiredService<DdtDbContext>();
        string subject = image.Id.ToString("D");
        AuditEvent deleted = await database.AuditEvents.SingleAsync(e => e.Action == AuditActions.ImageDeleted && e.SubjectId == subject, cancellationToken);

        Assert.Equal($"noble, a raw disk image, SHA-256 {image.Sha256}.", deleted.Detail);
    }

    [Fact]
    public async Task RefusesToDeleteAnImageMachinesAreWaitingForOrInstalling()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        Image image = await application.SeedImageAsync(RandomNumberGenerator.GetBytes(64));
        Guid deploymentId = await application.AddAssignedRunAsync(ArtifactKind.Image, image.Id, image.Sha256);

        Assert.Equal(HttpStatusCode.Conflict, (await administrator.DeleteAsync($"{Images}/{image.Id}")).StatusCode);

        await SetStateAsync(deploymentId, DeploymentState.Running);
        Assert.Equal(HttpStatusCode.Conflict, (await administrator.DeleteAsync($"{Images}/{image.Id}")).StatusCode);
        Assert.True(File.Exists(Store.ObjectPath(image.Sha256)));

        // A finished run keeps its copy of the image's details, so the image can go.
        await SetStateAsync(deploymentId, DeploymentState.Done);
        Assert.Equal(HttpStatusCode.NoContent, (await administrator.DeleteAsync($"{Images}/{image.Id}")).StatusCode);
        Assert.False(File.Exists(Store.ObjectPath(image.Sha256)));
    }

    [Fact]
    public async Task KeepsTheStoredFileWhileAnotherImageOfItRemains()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        byte[] content = RandomNumberGenerator.GetBytes(64);
        Image first = await application.SeedImageAsync(content, wimIndex: 1);
        Image second = await application.SeedImageAsync(content, wimIndex: 2);

        Assert.Equal(HttpStatusCode.NoContent, (await administrator.DeleteAsync($"{Images}/{first.Id}")).StatusCode);
        Assert.True(File.Exists(Store.ObjectPath(first.Sha256)));

        Assert.Equal(HttpStatusCode.NoContent, (await administrator.DeleteAsync($"{Images}/{second.Id}")).StatusCode);
        Assert.False(File.Exists(Store.ObjectPath(first.Sha256)));
    }

    // Each change carries what the library page patches its list with, so it never loads the list again.
    [Fact]
    public async Task PushesLibraryChangesToSignedInViewers()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        using SignedInClient viewer = await application.SignInAsync(DdtRoleNames.Viewer);
        await using LiveListener live = await LiveListener.StartAsync(application, viewer);
        ChannelReader<ImageSummary> changes = live.Listen<ImageSummary>(LiveEvents.ImageChanged);
        ChannelReader<ImagesRemovedEvent> removals = live.Listen<ImagesRemovedEvent>(LiveEvents.ImagesRemoved);

        ImageUploadSession session = await administrator.UploadAsync(TestWim.Create(TestWim.X64));
        ImageSummary added = Assert.Single(await RegisteredMachine.ReadAsync<IReadOnlyList<ImageSummary>>(
            await administrator.CompleteUploadAsync(session.Id)));

        Assert.Equal(added, await LiveListener.NextAsync(changes, i => i.Id == added.Id));

        Assert.Equal(HttpStatusCode.NoContent, (await administrator.DeleteAsync($"{Images}/{added.Id}")).StatusCode);
        Assert.Equal([added.Id], (await LiveListener.NextAsync(removals)).ImageIds);
    }
}
