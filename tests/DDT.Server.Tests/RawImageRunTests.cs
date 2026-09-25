// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using DDT.Contracts.Agents;
using DDT.Contracts.Deployments;
using DDT.Contracts.Images;
using DDT.Contracts.Machines;
using DDT.Contracts.Sequences;
using DDT.Core.CloudInit;
using DDT.Server.Authentication;
using DDT.Server.Images;
using DDT.Server.Machines;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DDT.Server.Tests;

// Runs of sequences that write a raw disk image, and who may let one that will not start with Secure Boot on run.
public sealed class RawImageRunTests(DdtApplication application) : IClassFixture<DdtApplication>
{
    private Task<Image> DiskImageAsync(ImageBootCapability capability = ImageBootCapability.SecureBootOk) =>
        application.SeedRawImageAsync(RandomNumberGenerator.GetBytes(4096), capability, name: $"noble {Guid.NewGuid():N}", installedBytes: 3_500_000_000);

    private async Task<SequenceView> LinuxAsync(Image image) =>
        await (await application.AdministratorAsync()).CreatedSequenceAsync(SequenceRequests.Linux(image.Id));

    private Task<List<string?>> AuditAsync(Guid runId)
    {
        string subject = runId.ToString("D");

        return application.QueryAsync(database => database.AuditEvents
            .Where(e => e.SubjectId == subject && e.Action == AuditActions.DeploymentAssigned)
            .Select(e => e.Detail)
            .ToListAsync(TestContext.Current.CancellationToken));
    }

    private static async Task<ValidationProblemDetails> ValidationAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        return (await response.Content.ReadFromJsonAsync<ValidationProblemDetails>(TestJson.Options))!;
    }

    [Fact]
    public async Task HandsTheAgentTheCompressedDiskWithWhatItHolds()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        using DeployingMachine machine = await DeployingMachine.RegisterAsync(application, [DeployingMachine.Disk(0)]);
        Image image = await DiskImageAsync();
        SequenceView sequence = await LinuxAsync(image);

        DeploymentSummary run = await administrator.AssignedAsync(machine.Id, sequence.Id, "LINUX-01");
        AgentRun handed = Assert.IsType<AgentRun>((await machine.NextAsync()).Run);

        Assert.Equal(
            [new AgentRunImage(image.Id, image.Name, image.Sha256, image.SizeBytes, 0, 3_500_000_000, ImageKind.RawDisk, ImageBootCapability.SecureBootOk)],
            handed.Images);
        Assert.False(handed.AllowSecureBootMismatch);
        Assert.Equal(2, handed.Sequence.Version);
        Assert.Equal("LINUX-01", handed.ComputerName);
        Assert.False((await administrator.RunAsync(run.Id)).AllowSecureBootMismatch);

        // The agent downloads the compressed disk like any file of its run.
        HttpResponseMessage file = await machine.Agent.RunFileAsync(machine.Id, machine.Token, run.Id, image.Sha256);
        Assert.Equal(HttpStatusCode.OK, file.StatusCode);
        Assert.Equal(image.SizeBytes, (await file.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken)).Length);
    }

    [Fact]
    public async Task AnAgentOlderThanRawDisksGetsNoSuchRun()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        using AgentClient agent = new(application.CreateDefaultClient(), TestRemoteAddress.Unique());
        AgentRegistration older = AgentClient.Registration(Guid.NewGuid().ToString("D"), RuleRequests.RandomMac()) with
        {
            SequenceVersion = 1,
            Disks = [DeployingMachine.Disk(0)],
        };
        AgentRegistrationResult registered = await RegisteredMachine.ReadAsync<AgentRegistrationResult>(await agent.RegisterAsync(older));
        SequenceView sequence = await LinuxAsync(await DiskImageAsync());

        await administrator.AssignedAsync(registered.MachineId, sequence.Id, "LINUX-02");
        AgentNextResult next = await RegisteredMachine.ReadAsync<AgentNextResult>(await agent.NextAsync(registered.MachineId, registered.Token!));

        Assert.Null(next.Run);
    }

    [Fact]
    public async Task AnAgentOlderThanRawDisksIsNotOfferedSuchASequenceAtTheMachine()
    {
        string operatorName = await application.CreateUserAsync(DdtRoleNames.Operator);
        using DeployingMachine machine = await DeployingMachine.SignedInAsync(
            application,
            operatorName,
            [DeployingMachine.Disk(0)],
            sequenceVersion: 1);
        SequenceView sequence = await LinuxAsync(await DiskImageAsync());

        IReadOnlyList<AgentSequenceChoice> choices =
            await RegisteredMachine.ReadAsync<IReadOnlyList<AgentSequenceChoice>>(await machine.Agent.SequencesAsync(machine.Id, machine.Token));
        HttpResponseMessage picked = await machine.Agent.PickRunAsync(machine.Id, machine.Token, new AgentRunRequest(sequence.Id, 0, "LINUX-03"));

        Assert.DoesNotContain(choices, choice => choice.Id == sequence.Id);
        Assert.Equal(HttpStatusCode.Conflict, picked.StatusCode);
        ProblemDetails? refusal = await picked.Content.ReadFromJsonAsync<ProblemDetails>(TestJson.Options, TestContext.Current.CancellationToken);
        Assert.StartsWith($"{sequence.Name} needs a newer agent than this machine runs.", refusal?.Title, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CountsTheDiskAndTheSeedButNotTheDownloadAtTheMachine()
    {
        string operatorName = await application.CreateUserAsync(DdtRoleNames.Operator);
        using DeployingMachine machine = await DeployingMachine.SignedInAsync(application, operatorName, [DeployingMachine.Disk(0)]);
        Image image = await DiskImageAsync(ImageBootCapability.NotSigned);
        SequenceView sequence = await LinuxAsync(image);

        AgentSequenceChoice choice = Assert.Single(
            await RegisteredMachine.ReadAsync<IReadOnlyList<AgentSequenceChoice>>(await machine.Agent.SequencesAsync(machine.Id, machine.Token)),
            c => c.Id == sequence.Id);

        Assert.Equal(3_500_000_000 + CloudInitSeed.DiskBytes, choice.RequiredBytes);
        Assert.True(choice.ErasesDisk);
        Assert.True(choice.NeedsComputerName);
        Assert.Equal(image.Name, choice.RawImageName);
        Assert.Equal(ImageBootCapability.NotSigned, choice.RawImageBootCapability);
    }

    [Fact]
    public async Task AskedForTheOverrideOnlyWhereTheMachineSaysSecureBootIsOn()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        using DeployingMachine on = await DeployingMachine.RegisterAsync(application, [DeployingMachine.Disk(0)], secureBootEnabled: true);
        using DeployingMachine unknown = await DeployingMachine.RegisterAsync(application, [DeployingMachine.Disk(0)]);
        Image image = await DiskImageAsync(ImageBootCapability.NotSigned);
        SequenceView sequence = await LinuxAsync(image);

        ValidationProblemDetails refused = await ValidationAsync(await administrator.AssignAsync(on.Id, sequence.Id, "LINUX-03"));
        Assert.Equal(
            [$"{image.Name} will not start with Secure Boot on, and this machine has Secure Boot on. Allow it for this run, or turn Secure Boot off in the machine's firmware first."],
            refused.Errors["allowSecureBootMismatch"]);

        DeploymentSummary allowed = (await RegisteredMachine.ReadAsync<MachineSummary>(
            await administrator.AssignAsync(on.Id, sequence.Id, "LINUX-03", allowSecureBootMismatch: true))).Deployment!;
        Assert.True(Assert.IsType<AgentRun>((await on.NextAsync()).Run).AllowSecureBootMismatch);
        Assert.True((await administrator.RunAsync(allowed.Id)).AllowSecureBootMismatch);
        Assert.Equal(
            [$"{sequence.Name}, revision 1, to machine {on.Id:D}. It may write {image.Name} although it will not start with Secure Boot on."],
            await AuditAsync(allowed.Id));

        // Where the machine did not say, its agent checks the firmware itself before it writes.
        DeploymentSummary unasked = await administrator.AssignedAsync(unknown.Id, sequence.Id, "LINUX-04");
        Assert.False(Assert.IsType<AgentRun>((await unknown.NextAsync()).Run).AllowSecureBootMismatch);
        Assert.Equal([$"{sequence.Name}, revision 1, to machine {unknown.Id:D}."], await AuditAsync(unasked.Id));
    }

    [Fact]
    public async Task KeepsNoOverrideForAnImageThatStartsWithSecureBootOn()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        using DeployingMachine machine = await DeployingMachine.RegisterAsync(application, [DeployingMachine.Disk(0)], secureBootEnabled: true);
        SequenceView sequence = await LinuxAsync(await DiskImageAsync());

        DeploymentSummary run = (await RegisteredMachine.ReadAsync<MachineSummary>(
            await administrator.AssignAsync(machine.Id, sequence.Id, "LINUX-05", allowSecureBootMismatch: true))).Deployment!;

        Assert.False((await administrator.RunAsync(run.Id)).AllowSecureBootMismatch);
    }

    [Fact]
    public async Task TheTechnicianAllowsTheImageAtTheMachine()
    {
        string operatorName = await application.CreateUserAsync(DdtRoleNames.Operator);
        using DeployingMachine machine = await DeployingMachine.SignedInAsync(application, operatorName, [DeployingMachine.Disk(0)], secureBootEnabled: true);
        Image image = await DiskImageAsync(ImageBootCapability.Unknown);
        SequenceView sequence = await LinuxAsync(image);

        ValidationProblemDetails refused = await ValidationAsync(
            await machine.Agent.PickRunAsync(machine.Id, machine.Token, new AgentRunRequest(sequence.Id, 0, "LINUX-06")));
        Assert.Contains("allowSecureBootMismatch", refused.Errors.Keys);

        AgentRun picked = await RegisteredMachine.ReadAsync<AgentRun>(
            await machine.Agent.PickRunAsync(machine.Id, machine.Token, new AgentRunRequest(sequence.Id, 0, "LINUX-06", AllowSecureBootMismatch: true)));

        Assert.True(picked.AllowSecureBootMismatch);
        Assert.Equal(ImageBootCapability.Unknown, Assert.Single(picked.Images).BootCapability);
        Assert.EndsWith(
            $"chosen at the machine. It may write {image.Name} although it may not start with Secure Boot on.",
            Assert.Single(await AuditAsync(picked.Id)),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnApprovalOfARulesSequenceCarriesTheOverride()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        using DeployingMachine machine = await DeployingMachine.RegisterAsync(application, [DeployingMachine.Disk(0)], secureBootEnabled: true);
        Image image = await DiskImageAsync(ImageBootCapability.NotSigned);
        SequenceView sequence = await administrator.CreatedSequenceAsync(SequenceRequests.Definition(
            new WriteRawImageStep { Id = Guid.NewGuid(), Name = "Write the disk", ImageId = image.Id }));
        await administrator.CreatedRuleAsync(RuleRequests.MacRule(sequence.Id, machine.Registration.PrimaryMac));

        HttpResponseMessage refused = await administrator.ApproveAsync(machine.Id, sequence.Id);
        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        Assert.Equal(MachineState.Pending, (await machine.NextAsync()).State);

        MachineSummary approved = await RegisteredMachine.ReadAsync<MachineSummary>(
            await administrator.ApproveAsync(machine.Id, sequence.Id, allowSecureBootMismatch: true));

        Assert.Equal(MachineState.Approved, approved.State);
        Assert.True((await administrator.RunAsync(approved.Deployment!.Id)).AllowSecureBootMismatch);
        Assert.EndsWith(
            "It may write " + image.Name + " although it will not start with Secure Boot on.",
            Assert.Single(await AuditAsync(approved.Deployment.Id)),
            StringComparison.Ordinal);
    }
}
