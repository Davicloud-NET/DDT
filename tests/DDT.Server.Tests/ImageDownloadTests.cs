using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using DDT.Contracts.Deployments;
using DDT.Contracts.Machines;
using DDT.Server.Images;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DDT.Server.Tests;

public sealed class ImageDownloadTests(DdtApplication application) : IClassFixture<DdtApplication>
{
    private async Task<(Image Image, byte[] Content)> ImageAsync()
    {
        byte[] content = RandomNumberGenerator.GetBytes(64 * 1024);

        return (await application.SeedImageAsync(content), content);
    }

    private async Task<Guid> AssignAsync(Guid machineId, Guid imageId)
    {
        SignedInClient administrator = await application.AdministratorAsync();

        MachineSummary summary = await RegisteredMachine.ReadAsync<MachineSummary>(
            await administrator.PostAsync($"/api/machines/{machineId}/deployments", new AssignImageRequest(imageId, null)));

        return summary.Deployment!.Id;
    }

    private async Task<DeployingMachine> ApprovedAsync() =>
        await DeployingMachine.ApprovedAsync(application, await application.AdministratorAsync());

    [Fact]
    public async Task ServesTheAssignedImageWithRangesAndAStrongTag()
    {
        using DeployingMachine machine = await ApprovedAsync();
        (Image image, byte[] content) = await ImageAsync();
        await AssignAsync(machine.Id, image.Id);

        HttpResponseMessage head = await machine.Agent.ImageAsync(machine.Id, machine.Token, image.Sha256, HttpMethod.Head);

        Assert.Equal(HttpStatusCode.OK, head.StatusCode);
        Assert.Equal(content.Length, head.Content.Headers.ContentLength);
        Assert.Equal($"\"{image.Sha256}\"", head.Headers.ETag?.Tag);
        Assert.False(head.Headers.ETag?.IsWeak);
        Assert.Contains("bytes", head.Headers.AcceptRanges);
        Assert.Empty(await head.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken));

        HttpResponseMessage whole = await machine.Agent.ImageAsync(machine.Id, machine.Token, image.Sha256);

        Assert.Equal(HttpStatusCode.OK, whole.StatusCode);
        Assert.Equal("application/octet-stream", whole.Content.Headers.ContentType?.MediaType);
        Assert.Equal(content, await whole.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken));

        HttpResponseMessage resumed = await machine.Agent.ImageAsync(machine.Id, machine.Token, image.Sha256, range: new RangeHeaderValue(40_000, null));

        Assert.Equal(HttpStatusCode.PartialContent, resumed.StatusCode);
        Assert.Equal(40_000, resumed.Content.Headers.ContentRange?.From);
        Assert.Equal(content.Length, resumed.Content.Headers.ContentRange?.Length);
        Assert.Equal(content[40_000..], await resumed.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken));

        HttpResponseMessage complete = await machine.Agent.ImageAsync(machine.Id, machine.Token, image.Sha256, range: new RangeHeaderValue(content.Length, null));

        Assert.Equal(HttpStatusCode.RequestedRangeNotSatisfiable, complete.StatusCode);
        Assert.Equal(content.Length, complete.Content.Headers.ContentRange?.Length);

        // The URL may carry the hash in either case; the stored one names the file.
        Assert.Equal(HttpStatusCode.OK, (await machine.Agent.ImageAsync(machine.Id, machine.Token, image.Sha256.ToUpperInvariant(), HttpMethod.Head)).StatusCode);
    }

    [Fact]
    public async Task ARunningDeploymentKeepsItsImageUntilItIsStopped()
    {
        using DeployingMachine machine = await ApprovedAsync();
        (Image image, _) = await ImageAsync();
        Guid deployment = await AssignAsync(machine.Id, image.Id);

        await machine.ReportOkAsync(deployment, DeploymentState.Running, DeploymentStep.Download, 10);
        Assert.Equal(HttpStatusCode.PartialContent, (await machine.Agent.ImageAsync(machine.Id, machine.Token, image.Sha256, range: new RangeHeaderValue(100, null))).StatusCode);

        (await (await application.AdministratorAsync()).DeleteAsync($"/api/machines/{machine.Id}/deployments/current")).EnsureSuccessStatusCode();

        Assert.Equal(HttpStatusCode.Unauthorized, (await machine.Agent.ImageAsync(machine.Id, machine.Token, image.Sha256, range: new RangeHeaderValue(200, null))).StatusCode);
    }

    [Fact]
    public async Task AMachineGetsOnlyTheImageOfItsOwnDeployment()
    {
        using DeployingMachine idle = await ApprovedAsync();
        using DeployingMachine assigned = await ApprovedAsync();
        (Image image, _) = await ImageAsync();
        (Image other, _) = await ImageAsync();
        await AssignAsync(assigned.Id, image.Id);

        HttpResponseMessage nothingAssigned = await idle.Agent.ImageAsync(idle.Id, idle.Token, image.Sha256);
        Assert.Equal(HttpStatusCode.Forbidden, nothingAssigned.StatusCode);
        Assert.Equal("This machine has no deployment of that image. Ask the server for the current deployment.", await TestDatabase.TitleAsync(nothingAssigned));

        Assert.Equal(HttpStatusCode.Forbidden, (await assigned.Agent.ImageAsync(assigned.Id, assigned.Token, other.Sha256)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await assigned.Agent.ImageAsync(assigned.Id, assigned.Token, "..%2F..%2Fkeys")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await idle.Agent.ImageAsync(assigned.Id, idle.Token, image.Sha256)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await idle.Agent.ImageAsync(assigned.Id, "not-a-token", image.Sha256)).StatusCode);
    }

    [Fact]
    public async Task AMachineThatStartedOverHasNoImage()
    {
        using DeployingMachine machine = await ApprovedAsync();
        (Image image, _) = await ImageAsync();
        await AssignAsync(machine.Id, image.Id);
        string session = machine.Token;

        // Registered again without its resume token: Pending, and every earlier token is dead.
        Assert.Equal(MachineState.Pending, (await machine.RegisterAgainAsync()).State);

        Assert.Equal(HttpStatusCode.Unauthorized, (await machine.Agent.ImageAsync(machine.Id, session, image.Sha256)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await machine.Agent.ImageAsync(machine.Id, machine.Token, image.Sha256)).StatusCode);
    }

    [Fact]
    public async Task AMissingFileIsNotFound()
    {
        using DeployingMachine machine = await ApprovedAsync();
        (Image image, _) = await ImageAsync();
        await AssignAsync(machine.Id, image.Id);

        File.Delete(application.Services.GetRequiredService<ImageStore>().ObjectPath(image.Sha256));

        HttpResponseMessage missing = await machine.Agent.ImageAsync(machine.Id, machine.Token, image.Sha256, HttpMethod.Head);

        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }
}
