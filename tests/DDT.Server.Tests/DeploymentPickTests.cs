// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Net;
using System.Security.Cryptography;
using DDT.Contracts.Agents;
using DDT.Contracts.Deployments;
using DDT.Contracts.Machines;
using DDT.Server.Authentication;
using DDT.Server.Data;
using DDT.Server.Images;
using DDT.Server.Machines;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DDT.Server.Tests;

public sealed class DeploymentPickTests(DdtApplication application) : IClassFixture<DdtApplication>
{
    private Task<Image> ImageAsync(string? architecture = "x64") => application.SeedImageAsync(RandomNumberGenerator.GetBytes(4096), architecture);

    private static async Task<HttpResponseMessage> PickAsync(DeployingMachine machine, Guid imageId, int? diskNumber = 0, string? computerName = null) =>
        await machine.Agent.PickAsync(machine.Id, machine.Token, new AgentPickRequest(imageId, diskNumber, computerName));

    private async Task ChangeUserAsync(string userName, Func<UserManager<DdtUser>, DdtUser, Task> change)
    {
        using IServiceScope scope = application.Services.CreateScope();
        UserManager<DdtUser> users = scope.ServiceProvider.GetRequiredService<UserManager<DdtUser>>();

        await change(users, (await users.FindByNameAsync(userName))!);
    }

    [Fact]
    public async Task OnlyAMachineSomeoneSignedInAtMayChooseAnImage()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        using DeployingMachine approvedOnTheWeb = await DeployingMachine.ApprovedAsync(application, administrator);
        Image image = await ImageAsync();

        Assert.False((await approvedOnTheWeb.NextAsync()).CanPickImage);

        HttpResponseMessage list = await approvedOnTheWeb.Agent.ImagesAsync(approvedOnTheWeb.Id, approvedOnTheWeb.Token);
        Assert.Equal(HttpStatusCode.Forbidden, list.StatusCode);
        Assert.StartsWith("Only an operator or administrator signed in at this machine can choose an image.", await TestDatabase.TitleAsync(list), StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.Conflict, (await PickAsync(approvedOnTheWeb, image.Id)).StatusCode);

        string operatorName = await application.CreateUserAsync(DdtRoleNames.Operator);
        using DeployingMachine signedIn = await DeployingMachine.SignedInAsync(application, operatorName);

        AgentNextResult next = await signedIn.NextAsync();

        Assert.Equal(MachineState.Approved, next.State);
        Assert.True(next.CanPickImage);
        Assert.Null(next.Deployment);
        Assert.Equal(HttpStatusCode.OK, (await signedIn.Agent.ImagesAsync(signedIn.Id, signedIn.Token)).StatusCode);

        // A waiting machine holds only a poll token, which reaches none of this.
        using DeployingMachine waiting = await DeployingMachine.RegisterAsync(application);
        Assert.Equal(HttpStatusCode.Forbidden, (await waiting.Agent.ImagesAsync(waiting.Id, waiting.Token)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await PickAsync(waiting, image.Id)).StatusCode);
    }

    // Token generations are small numbers that machines share, so only the machine id keeps one machine's session
    // token away from another machine's images and deployments.
    [Fact]
    public async Task AMachineCannotListOrChooseForAnotherMachine()
    {
        string operatorName = await application.CreateUserAsync(DdtRoleNames.Operator);
        using DeployingMachine first = await DeployingMachine.SignedInAsync(application, operatorName);
        using DeployingMachine second = await DeployingMachine.SignedInAsync(application, operatorName);
        Image image = await ImageAsync();

        Assert.Equal(HttpStatusCode.Forbidden, (await second.Agent.ImagesAsync(first.Id, second.Token)).StatusCode);
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await second.Agent.PickAsync(first.Id, second.Token, new AgentPickRequest(image.Id, 0, null))).StatusCode);
        Assert.Null((await first.NextAsync()).Deployment);
    }

    [Fact]
    public async Task ListsOnlyImagesThisAgentCanDeploy()
    {
        string operatorName = await application.CreateUserAsync(DdtRoleNames.Operator);
        using DeployingMachine machine = await DeployingMachine.SignedInAsync(application, operatorName);
        Image x64 = await ImageAsync();
        Image arm = await ImageAsync("arm64");

        IReadOnlyList<AgentImageChoice> images = await RegisteredMachine.ReadAsync<IReadOnlyList<AgentImageChoice>>(
            await machine.Agent.ImagesAsync(machine.Id, machine.Token));

        AgentImageChoice choice = Assert.Single(images, i => i.Id == x64.Id);
        Assert.Equal(x64.Name, choice.Name);
        Assert.Equal("Professional", choice.Edition);
        Assert.Equal("en-US", choice.Language);
        Assert.Equal(x64.SizeBytes, choice.SizeBytes);
        Assert.Equal(x64.InstalledBytes, choice.InstalledBytes);
        Assert.DoesNotContain(images, i => i.Id == arm.Id);
        Assert.Equal([.. images.Select(i => i.Name).Order(StringComparer.OrdinalIgnoreCase)], images.Select(i => i.Name));
    }

    [Fact]
    public async Task ChoosingAnImageAssignsItOnTheChosenDisk()
    {
        string operatorName = await application.CreateUserAsync(DdtRoleNames.Operator);
        using DeployingMachine machine = await DeployingMachine.SignedInAsync(
            application,
            operatorName,
            [DeployingMachine.Disk(0), DeployingMachine.Disk(1, "Samsung SSD 990 PRO")]);
        Image image = await ImageAsync();

        AgentDeployment picked = await RegisteredMachine.ReadAsync<AgentDeployment>(await PickAsync(machine, image.Id, diskNumber: 1, computerName: "PC-0002"));

        Assert.Equal(DeploymentState.Assigned, picked.State);
        Assert.Equal(image.Id, picked.ImageId);
        Assert.Equal(image.Sha256, picked.Sha256);
        Assert.Equal(1, picked.DiskNumber);

        // A lost pick response loses nothing: the next poll carries the same deployment and disk.
        AgentNextResult next = await machine.NextAsync();

        Assert.Equal(picked, next.Deployment);
        Assert.False(next.CanPickImage);
        Assert.Equal("PC-0002", next.AssignedName);

        HttpResponseMessage again = await PickAsync(machine, image.Id);
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        Assert.Equal("This machine already has a deployment. It starts once the agent asks the server again.", await TestDatabase.TitleAsync(again));

        SignedInClient administrator = await application.AdministratorAsync();
        IReadOnlyList<MachineSummary> machines = await RegisteredMachine.ReadAsync<IReadOnlyList<MachineSummary>>(await administrator.GetAsync("/api/machines"));
        DeploymentSummary summary = Assert.IsType<DeploymentSummary>(Assert.Single(machines, m => m.Id == machine.Id).Deployment);

        Assert.Equal(DeploymentSource.Console, summary.Source);
        Assert.Equal(operatorName, summary.RequestedBy);

        string subject = picked.Id.ToString("D");
        AuditEvent audit = await application.QueryAsync(database => database.AuditEvents.SingleAsync(
            e => e.SubjectId == subject,
            TestContext.Current.CancellationToken));
        Guid operatorId = await application.QueryAsync(database => database.Users
            .Where(u => u.UserName == operatorName)
            .Select(u => u.Id)
            .SingleAsync(TestContext.Current.CancellationToken));

        Assert.Equal(AuditActions.DeploymentAssigned, audit.Action);
        Assert.Equal(operatorId, audit.ActorUserId);
        Assert.Equal(machine.Id, audit.ActorMachineId);
        Assert.Equal(operatorName, audit.ActorName);
        Assert.Contains(machine.Id.ToString("D"), audit.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RefusesAChoiceTheMachineCannotDeploy()
    {
        string operatorName = await application.CreateUserAsync(DdtRoleNames.Operator);
        using DeployingMachine machine = await DeployingMachine.SignedInAsync(application, operatorName);
        Image arm = await ImageAsync("arm64");
        Image image = await ImageAsync();

        HttpResponseMessage armImage = await PickAsync(machine, arm.Id);
        Assert.Equal(HttpStatusCode.Conflict, armImage.StatusCode);
        Assert.Equal($"{arm.Name} is an arm64 image, and DDT deploys only x64 Windows. Choose an x64 image.", await TestDatabase.TitleAsync(armImage));
        Assert.Equal(HttpStatusCode.NotFound, (await PickAsync(machine, Guid.NewGuid())).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await PickAsync(machine, image.Id, diskNumber: -1)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await PickAsync(machine, image.Id, computerName: "PC 0001")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await PickAsync(machine, image.Id, computerName: "-PC")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await PickAsync(machine, image.Id, computerName: "PC-00000000000001")).StatusCode);

        // Without a domain, Windows makes a name up.
        Assert.Equal(HttpStatusCode.OK, (await PickAsync(machine, image.Id, computerName: null)).StatusCode);
    }

    // Holding the library lock stands in for an image deletion that runs between the lookup and the save.
    [Fact]
    public async Task AChoiceWaitsForTheImageLibrary()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        string operatorName = await application.CreateUserAsync(DdtRoleNames.Operator);
        using DeployingMachine machine = await DeployingMachine.SignedInAsync(application, operatorName);
        Image image = await ImageAsync();
        ImageStore store = application.Services.GetRequiredService<ImageStore>();
        Task<HttpResponseMessage> picking;

        await store.LibraryLock.WaitAsync(cancellationToken);

        try
        {
            picking = PickAsync(machine, image.Id);
            await Task.WhenAny(picking, Task.Delay(TimeSpan.FromSeconds(1), cancellationToken));
            Assert.False(picking.IsCompleted);

            await application.QueryAsync(database => database.Images.Where(i => i.Id == image.Id).ExecuteDeleteAsync(cancellationToken));
        }
        finally
        {
            store.LibraryLock.Release();
        }

        HttpResponseMessage response = await picking;

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("The image no longer exists. Choose another image.", await TestDatabase.TitleAsync(response));
        Assert.Null((await machine.NextAsync()).Deployment);
    }

    [Fact]
    public async Task TheSignerMustStillBeAllowedToDeploy()
    {
        string lockedOut = await application.CreateUserAsync(DdtRoleNames.Operator);
        string demoted = await application.CreateUserAsync(DdtRoleNames.Operator);
        using DeployingMachine one = await DeployingMachine.SignedInAsync(application, lockedOut);
        using DeployingMachine two = await DeployingMachine.SignedInAsync(application, demoted);

        Assert.True((await one.NextAsync()).CanPickImage);
        Assert.True((await two.NextAsync()).CanPickImage);

        await ChangeUserAsync(lockedOut, (users, user) => users.SetLockoutEndDateAsync(user, DateTimeOffset.UtcNow.AddHours(1)));
        await ChangeUserAsync(demoted, async (users, user) =>
        {
            await users.RemoveFromRoleAsync(user, DdtRoleNames.Operator);
            await users.AddToRoleAsync(user, DdtRoleNames.Viewer);
        });

        Assert.False((await one.NextAsync()).CanPickImage);
        Assert.False((await two.NextAsync()).CanPickImage);
        Assert.Equal(HttpStatusCode.Forbidden, (await two.Agent.ImagesAsync(two.Id, two.Token)).StatusCode);
    }

    [Fact]
    public async Task AFailedMachineOffersThePickerAgain()
    {
        string operatorName = await application.CreateUserAsync(DdtRoleNames.Operator);
        using DeployingMachine machine = await DeployingMachine.SignedInAsync(application, operatorName);
        Image image = await ImageAsync();

        AgentDeployment first = await RegisteredMachine.ReadAsync<AgentDeployment>(await PickAsync(machine, image.Id));
        await machine.ReportOkAsync(first.Id, DeploymentState.Running, DeploymentStep.Partition);
        await machine.ReportOkAsync(first.Id, DeploymentState.Failed, DeploymentStep.Apply, 30, "wimlib error 59: Unable to set the security descriptor.");

        AgentNextResult failed = await machine.NextAsync();

        Assert.Equal(MachineState.Failed, failed.State);
        Assert.True(failed.CanPickImage);
        Assert.Null(failed.Deployment);

        AgentDeployment second = await RegisteredMachine.ReadAsync<AgentDeployment>(await PickAsync(machine, image.Id));
        AgentNextResult next = await machine.NextAsync();

        Assert.NotEqual(first.Id, second.Id);
        Assert.Equal(MachineState.Approved, next.State);
        Assert.Equal(second.Id, next.Deployment?.Id);
    }
}
