// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using DDT.Contracts.Agents;
using DDT.Contracts.Machines;
using DDT.Contracts.Packages;
using DDT.Contracts.Sequences;
using DDT.Server.Images;
using DDT.Server.Packages;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static DDT.Server.Tests.TestReports;

namespace DDT.Server.Tests;

public sealed class RunFileTests(DdtApplication application) : IClassFixture<DdtApplication>
{
    private async Task<(Image Image, byte[] Content)> ImageAsync()
    {
        byte[] content = RandomNumberGenerator.GetBytes(64 * 1024);

        return (await application.SeedImageAsync(content), content);
    }

    // The run of a sequence that applies the image, as the machine's agent receives it.
    private async Task<AgentRun> AssignAsync(DeployingMachine machine, Guid imageId, params SequenceStep[] more)
    {
        SignedInClient administrator = await application.AdministratorAsync();
        SequenceView sequence = await administrator.CreatedSequenceAsync(SequenceRequests.Definition([.. SequenceRequests.Minimal(imageId).Steps, .. more]));

        await administrator.AssignedAsync(machine.Id, sequence.Id);

        return (await machine.NextAsync()).Run!;
    }

    private async Task<DeployingMachine> ApprovedAsync() =>
        await DeployingMachine.ApprovedAsync(application, await application.AdministratorAsync());

    [Fact]
    public async Task ServesTheRunsImageWithRangesAndAStrongTag()
    {
        using DeployingMachine machine = await ApprovedAsync();
        (Image image, byte[] content) = await ImageAsync();
        AgentRun run = await AssignAsync(machine, image.Id);

        HttpResponseMessage head = await machine.Agent.RunFileHeadAsync(machine.Id, machine.Token, run.Id, image.Sha256);

        Assert.Equal(HttpStatusCode.OK, head.StatusCode);
        Assert.Equal(content.Length, head.Content.Headers.ContentLength);
        Assert.Equal($"\"{image.Sha256}\"", head.Headers.ETag?.Tag);
        Assert.False(head.Headers.ETag?.IsWeak);
        Assert.Contains("bytes", head.Headers.AcceptRanges);
        Assert.Empty(await head.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken));

        HttpResponseMessage whole = await machine.Agent.RunFileAsync(machine.Id, machine.Token, run.Id, image.Sha256);

        Assert.Equal(HttpStatusCode.OK, whole.StatusCode);
        Assert.Equal("application/octet-stream", whole.Content.Headers.ContentType?.MediaType);
        Assert.Equal(content, await whole.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken));

        HttpResponseMessage resumed = await machine.Agent.RunFileRangeAsync(machine.Id, machine.Token, run.Id, image.Sha256, new RangeHeaderValue(40_000, null));

        Assert.Equal(HttpStatusCode.PartialContent, resumed.StatusCode);
        Assert.Equal(40_000, resumed.Content.Headers.ContentRange?.From);
        Assert.Equal(content.Length, resumed.Content.Headers.ContentRange?.Length);
        Assert.Equal(content[40_000..], await resumed.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken));

        HttpResponseMessage complete = await machine.Agent.RunFileRangeAsync(machine.Id, machine.Token, run.Id, image.Sha256, new RangeHeaderValue(content.Length, null));

        Assert.Equal(HttpStatusCode.RequestedRangeNotSatisfiable, complete.StatusCode);
        Assert.Equal(content.Length, complete.Content.Headers.ContentRange?.Length);

        // The URL may carry the hash in either case; the stored one names the file.
        Assert.Equal(HttpStatusCode.OK, (await machine.Agent.RunFileHeadAsync(machine.Id, machine.Token, run.Id, image.Sha256.ToUpperInvariant())).StatusCode);
    }

    [Fact]
    public async Task ServesThePackagesOfTheRun()
    {
        using DeployingMachine machine = await ApprovedAsync();
        (Image image, _) = await ImageAsync();
        Package drivers = await application.SeedPackageAsync(PackageKind.Drivers, new HardwareModel(null, "Virtual Machine"));
        Package unrelated = await application.SeedPackageAsync(PackageKind.Files);
        AgentRun run = await AssignAsync(machine, image.Id, new InjectDriversStep { Id = Guid.NewGuid(), Name = "Drivers" });

        Assert.Equal(drivers.Sha256, Assert.Single(run.Packages).Sha256);
        Assert.Equal(HttpStatusCode.OK, (await machine.Agent.RunFileHeadAsync(machine.Id, machine.Token, run.Id, drivers.Sha256)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await machine.Agent.RunFileHeadAsync(machine.Id, machine.Token, run.Id, unrelated.Sha256)).StatusCode);
    }

    [Fact]
    public async Task ARunningRunKeepsItsFilesUntilItIsStopped()
    {
        using DeployingMachine machine = await ApprovedAsync();
        (Image image, _) = await ImageAsync();
        AgentRun run = await AssignAsync(machine, image.Id);

        await machine.ReportOkAsync(run.Id, Running(Step(run.Sequence.Steps[0], StepState.Done), Step(run.Sequence.Steps[1], StepState.Running)));
        Assert.Equal(HttpStatusCode.PartialContent, (await machine.Agent.RunFileRangeAsync(machine.Id, machine.Token, run.Id, image.Sha256, new RangeHeaderValue(100, null))).StatusCode);

        (await (await application.AdministratorAsync()).EndCurrentAsync(machine.Id)).EnsureSuccessStatusCode();

        Assert.Equal(HttpStatusCode.Unauthorized, (await machine.Agent.RunFileRangeAsync(machine.Id, machine.Token, run.Id, image.Sha256, new RangeHeaderValue(200, null))).StatusCode);
    }

    [Fact]
    public async Task AMachineGetsOnlyTheFilesOfItsOwnRun()
    {
        using DeployingMachine idle = await ApprovedAsync();
        using DeployingMachine assigned = await ApprovedAsync();
        using DeployingMachine neighbour = await ApprovedAsync();
        (Image image, _) = await ImageAsync();
        (Image other, _) = await ImageAsync();
        AgentRun run = await AssignAsync(assigned, image.Id);

        // The other image is another machine's to download right now, but never this run's.
        AgentRun neighbourRun = await AssignAsync(neighbour, other.Id);
        Assert.Equal(HttpStatusCode.OK, (await neighbour.Agent.RunFileHeadAsync(neighbour.Id, neighbour.Token, neighbourRun.Id, other.Sha256)).StatusCode);

        HttpResponseMessage nothingAssigned = await idle.Agent.RunFileAsync(idle.Id, idle.Token, run.Id, image.Sha256);
        Assert.Equal(HttpStatusCode.Forbidden, nothingAssigned.StatusCode);
        Assert.Equal("This machine's run has no such file. Ask the server for the current run.", await TestDatabase.TitleAsync(nothingAssigned));

        Assert.Equal(HttpStatusCode.Forbidden, (await assigned.Agent.RunFileAsync(assigned.Id, assigned.Token, run.Id, other.Sha256)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await assigned.Agent.RunFileAsync(assigned.Id, assigned.Token, Guid.NewGuid(), image.Sha256)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await assigned.Agent.RunFileAsync(assigned.Id, assigned.Token, run.Id, "..%2F..%2Fkeys")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await idle.Agent.RunFileAsync(assigned.Id, idle.Token, run.Id, image.Sha256)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await idle.Agent.RunFileAsync(assigned.Id, "not-a-token", run.Id, image.Sha256)).StatusCode);
    }

    [Fact]
    public async Task AMachineThatStartedOverHasNoFiles()
    {
        using DeployingMachine machine = await ApprovedAsync();
        (Image image, _) = await ImageAsync();
        AgentRun run = await AssignAsync(machine, image.Id);
        string session = machine.Token;

        // Registered again without its resume token: Pending, and every earlier token is dead.
        Assert.Equal(MachineState.Pending, (await machine.RegisterAgainAsync()).State);

        Assert.Equal(HttpStatusCode.Unauthorized, (await machine.Agent.RunFileAsync(machine.Id, session, run.Id, image.Sha256)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await machine.Agent.RunFileAsync(machine.Id, machine.Token, run.Id, image.Sha256)).StatusCode);
    }

    [Fact]
    public async Task AMissingFileIsNotFound()
    {
        using DeployingMachine machine = await ApprovedAsync();
        (Image image, _) = await ImageAsync();
        AgentRun run = await AssignAsync(machine, image.Id);

        File.Delete(application.Services.GetRequiredService<ImageStore>().ObjectPath(image.Sha256));

        Assert.Equal(HttpStatusCode.NotFound, (await machine.Agent.RunFileHeadAsync(machine.Id, machine.Token, run.Id, image.Sha256)).StatusCode);
    }
}
