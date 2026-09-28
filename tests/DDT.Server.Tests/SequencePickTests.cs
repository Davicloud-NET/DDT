// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Net;
using System.Security.Cryptography;
using DDT.Contracts.Agents;
using DDT.Contracts.Deployments;
using DDT.Contracts.Machines;
using DDT.Contracts.Packages;
using DDT.Contracts.Rules;
using DDT.Contracts.Sequences;
using DDT.Server.Authentication;
using DDT.Server.Data;
using DDT.Server.Images;
using DDT.Server.Machines;
using DDT.Server.Packages;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DDT.Server.Tests;

public sealed class SequencePickTests(DdtApplication application) : IClassFixture<DdtApplication>
{
    private Task<Image> ImageAsync(string? architecture = "x64") => application.SeedImageAsync(RandomNumberGenerator.GetBytes(4096), architecture);

    private async Task<SequenceView> SequenceAsync(SequenceDefinition definition) =>
        await (await application.AdministratorAsync()).CreatedSequenceAsync(definition);

    private static async Task<HttpResponseMessage> PickAsync(DeployingMachine machine, Guid sequenceId, int? diskNumber = 0, string? computerName = null) =>
        await machine.Agent.PickRunAsync(machine.Id, machine.Token, new AgentRunRequest(sequenceId, diskNumber, computerName));

    private static async Task<IReadOnlyList<AgentSequenceChoice>> ChoicesAsync(DeployingMachine machine) =>
        await RegisteredMachine.ReadAsync<IReadOnlyList<AgentSequenceChoice>>(await machine.Agent.SequencesAsync(machine.Id, machine.Token));

    private async Task ChangeUserAsync(string userName, Func<UserManager<DdtUser>, DdtUser, Task> change)
    {
        using IServiceScope scope = application.Services.CreateScope();
        UserManager<DdtUser> users = scope.ServiceProvider.GetRequiredService<UserManager<DdtUser>>();

        await change(users, (await users.FindByNameAsync(userName))!);
    }

    [Fact]
    public async Task OnlyAMachineSomeoneSignedInAtMayChooseASequence()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        using DeployingMachine approvedOnTheWeb = await DeployingMachine.ApprovedAsync(application, administrator);
        SequenceView sequence = await SequenceAsync(SequenceRequests.ScriptOnly());

        Assert.False((await approvedOnTheWeb.NextAsync()).CanPickSequence);

        HttpResponseMessage list = await approvedOnTheWeb.Agent.SequencesAsync(approvedOnTheWeb.Id, approvedOnTheWeb.Token);
        Assert.Equal(HttpStatusCode.Forbidden, list.StatusCode);
        Assert.StartsWith("Only an operator or administrator signed in at this machine can choose a sequence.", await TestDatabase.TitleAsync(list), StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.Conflict, (await PickAsync(approvedOnTheWeb, sequence.Id)).StatusCode);

        string operatorName = await application.CreateUserAsync(DdtRoleNames.Operator);
        using DeployingMachine signedIn = await DeployingMachine.SignedInAsync(application, operatorName);

        AgentNextResult next = await signedIn.NextAsync();

        Assert.Equal(MachineState.Approved, next.State);
        Assert.True(next.CanPickSequence);
        Assert.False(next.CanPickImage);
        Assert.Null(next.Run);
        Assert.Equal(HttpStatusCode.OK, (await signedIn.Agent.SequencesAsync(signedIn.Id, signedIn.Token)).StatusCode);

        // A waiting machine holds only a poll token, which reaches none of this.
        using DeployingMachine waiting = await DeployingMachine.RegisterAsync(application);
        Assert.Equal(HttpStatusCode.Forbidden, (await waiting.Agent.SequencesAsync(waiting.Id, waiting.Token)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await PickAsync(waiting, sequence.Id)).StatusCode);
    }

    // A body that fails to bind is refused before the machine is looked up, even with another machine's token.
    [Fact]
    public async Task AChoiceWithoutABodyIsABadRequest()
    {
        string operatorName = await application.CreateUserAsync(DdtRoleNames.Operator);
        using DeployingMachine first = await DeployingMachine.SignedInAsync(application, operatorName);
        using DeployingMachine second = await DeployingMachine.SignedInAsync(application, operatorName);

        Assert.Equal(HttpStatusCode.BadRequest, (await second.Agent.PickRunAsync(first.Id, second.Token, "null")).StatusCode);
    }

    // Token generations are small numbers that machines share, so only the machine id keeps one machine's session
    // token away from another machine's sequences and runs.
    [Fact]
    public async Task AMachineCannotListOrChooseForAnotherMachine()
    {
        string operatorName = await application.CreateUserAsync(DdtRoleNames.Operator);
        using DeployingMachine first = await DeployingMachine.SignedInAsync(application, operatorName);
        using DeployingMachine second = await DeployingMachine.SignedInAsync(application, operatorName);
        SequenceView sequence = await SequenceAsync(SequenceRequests.ScriptOnly());

        Assert.Equal(HttpStatusCode.Forbidden, (await second.Agent.SequencesAsync(first.Id, second.Token)).StatusCode);
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await second.Agent.PickRunAsync(first.Id, second.Token, new AgentRunRequest(sequence.Id, 0, null))).StatusCode);
        Assert.Null((await application.MachineAsync(first.Id)).ActiveDeploymentId);
    }

    [Fact]
    public async Task ListsOnlySequencesThatCanRunWithWhatEachNeeds()
    {
        string operatorName = await application.CreateUserAsync(DdtRoleNames.Operator);
        using DeployingMachine machine = await DeployingMachine.SignedInAsync(application, operatorName);
        Image image = await ImageAsync();
        SequenceView erasing = await SequenceAsync(SequenceRequests.Minimal(image.Id));
        SequenceView script = await SequenceAsync(SequenceRequests.ScriptOnly());
        SequenceView broken = await SequenceAsync(SequenceRequests.Minimal((await ImageAsync("arm64")).Id));

        IReadOnlyList<AgentSequenceChoice> choices = await ChoicesAsync(machine);

        AgentSequenceChoice installs = Assert.Single(choices, c => c.Id == erasing.Id);
        Assert.Equal(erasing.Name, installs.Name);
        Assert.True(installs.ErasesDisk);
        Assert.False(installs.NeedsComputerName);
        Assert.False(installs.Suggested);

        // The partitions of the default sizes and the image, downloaded and applied.
        Assert.Equal((300 + 1024 + 16) * 1024L * 1024 + image.SizeBytes + image.InstalledBytes, installs.RequiredBytes);

        AgentSequenceChoice runs = Assert.Single(choices, c => c.Id == script.Id);
        Assert.False(runs.ErasesDisk);
        Assert.Equal(0, runs.RequiredBytes);

        Assert.DoesNotContain(choices, c => c.Id == broken.Id);
        Assert.Equal([.. choices.Select(c => c.Name).Order(StringComparer.OrdinalIgnoreCase)], choices.Select(c => c.Name));
    }

    [Fact]
    public async Task ChoosingASequenceAssignsItOnTheChosenDisk()
    {
        string operatorName = await application.CreateUserAsync(DdtRoleNames.Operator);
        using DeployingMachine machine = await DeployingMachine.SignedInAsync(
            application,
            operatorName,
            [DeployingMachine.Disk(0), DeployingMachine.Disk(1, "Samsung SSD 990 PRO")]);
        Image image = await ImageAsync();
        Package drivers = await application.SeedPackageAsync(PackageKind.Drivers, new HardwareModel(null, "Virtual Machine"));
        InjectDriversStep inject = new() { Id = Guid.NewGuid(), Name = "Drivers" };
        SequenceView sequence = await SequenceAsync(SequenceRequests.Definition([.. SequenceRequests.Minimal(image.Id).Steps, inject]));

        AgentRun picked = await RegisteredMachine.ReadAsync<AgentRun>(await PickAsync(machine, sequence.Id, diskNumber: 1, computerName: "PC-0002"));

        Assert.Equal(DeploymentState.Assigned, picked.State);
        Assert.Equal(sequence.Name, picked.SequenceName);
        Assert.Equal(SequenceRequests.Json(sequence.Definition), SequenceRequests.Json(picked.Sequence));
        Assert.Equal(1, picked.DiskNumber);
        Assert.Equal("PC-0002", picked.ComputerName);
        Assert.Equal([new AgentRunImage(image.Id, image.Name, image.Sha256, image.SizeBytes, image.WimIndex, image.InstalledBytes)], picked.Images);
        Assert.Equal([new AgentRunPackage(inject.Id, drivers.Name, drivers.Sha256, drivers.SizeBytes)], picked.Packages);

        // An edit after the choice changes nothing the agent is handed.
        SignedInClient administrator = await application.AdministratorAsync();
        SequenceDefinition edited = SequenceRequests.Definition([.. sequence.Definition.Steps, .. SequenceRequests.ScriptOnly().Steps]);
        (await administrator.SaveSequenceAsync(sequence, edited)).EnsureSuccessStatusCode();

        AgentNextResult next = await machine.NextAsync();

        Assert.False(next.CanPickSequence);
        Assert.Equal("PC-0002", next.AssignedName);
        Assert.Equal(SequenceRequests.Json(picked.Sequence), SequenceRequests.Json(next.Run!.Sequence));

        HttpResponseMessage again = await PickAsync(machine, sequence.Id);
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        Assert.Equal("This machine already has a run. It starts once the agent asks the server again.", await TestDatabase.TitleAsync(again));

        IReadOnlyList<MachineSummary> machines = await RegisteredMachine.ReadAsync<IReadOnlyList<MachineSummary>>(await administrator.GetAsync("/api/machines"));
        DeploymentSummary summary = Assert.IsType<DeploymentSummary>(Assert.Single(machines, m => m.Id == machine.Id).Deployment);

        Assert.Equal(picked.Id, summary.Id);
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
        Assert.Equal($"{sequence.Name}, revision 1, to machine {machine.Id:D}, chosen at the machine.", audit.Detail);
    }

    // A sequence that erases nothing takes no disk, whatever the agent sends.
    [Fact]
    public async Task ASequenceThatErasesNothingTakesNoDisk()
    {
        string operatorName = await application.CreateUserAsync(DdtRoleNames.Operator);
        using DeployingMachine machine = await DeployingMachine.SignedInAsync(application, operatorName);
        SequenceView sequence = await SequenceAsync(SequenceRequests.ScriptOnly());

        AgentRun picked = await RegisteredMachine.ReadAsync<AgentRun>(await PickAsync(machine, sequence.Id, diskNumber: -1));

        Assert.Null(picked.DiskNumber);
        Assert.Empty(picked.Images);

        using DeployingMachine without = await DeployingMachine.SignedInAsync(application, operatorName);
        AgentRun withoutDisk = await RegisteredMachine.ReadAsync<AgentRun>(await PickAsync(without, sequence.Id, diskNumber: null));

        Assert.Equal(DeploymentState.Assigned, withoutDisk.State);
        Assert.Null(withoutDisk.DiskNumber);
    }

    // The agent sends a disk with every choice it listed as erasing one. A choice without a disk was made before an
    // administrator changed the sequence to erase one, so nobody at the machine chose a disk or typed ERASE.
    [Fact]
    public async Task RefusesAChoiceWithoutADiskOfASequenceThatErasesOne()
    {
        string operatorName = await application.CreateUserAsync(DdtRoleNames.Operator);
        using DeployingMachine machine = await DeployingMachine.SignedInAsync(application, operatorName, [DeployingMachine.Disk(0)]);
        SequenceView sequence = await SequenceAsync(SequenceRequests.ScriptOnly());

        Assert.False(Assert.Single(await ChoicesAsync(machine), c => c.Id == sequence.Id).ErasesDisk);

        SignedInClient administrator = await application.AdministratorAsync();
        SequenceDefinition erasing = SequenceRequests.Definition([.. SequenceRequests.Minimal((await ImageAsync()).Id).Steps, .. sequence.Definition.Steps]);
        (await administrator.SaveSequenceAsync(sequence, erasing)).EnsureSuccessStatusCode();

        HttpResponseMessage refused = await PickAsync(machine, sequence.Id, diskNumber: null);

        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        Assert.Equal(
            $"{sequence.Name} erases a disk, and no disk was chosen for it at the machine. It was probably changed after the list was shown. Choose it again.",
            await TestDatabase.TitleAsync(refused));

        Machine stored = await application.MachineAsync(machine.Id);
        Assert.Null(stored.ActiveDeploymentId);
        Assert.Null(stored.LastDeploymentId);
        Assert.True((await machine.NextAsync()).CanPickSequence);
    }

    [Fact]
    public async Task RefusesAChoiceTheMachineCannotRun()
    {
        string operatorName = await application.CreateUserAsync(DdtRoleNames.Operator);
        using DeployingMachine machine = await DeployingMachine.SignedInAsync(application, operatorName);
        SequenceView broken = await SequenceAsync(SequenceRequests.Minimal((await ImageAsync("arm64")).Id));
        SequenceView sequence = await SequenceAsync(SequenceRequests.Minimal((await ImageAsync()).Id));

        HttpResponseMessage problem = await PickAsync(machine, broken.Id);
        Assert.Equal(HttpStatusCode.Conflict, problem.StatusCode);
        Assert.Equal($"{broken.Name} has a problem, so it cannot run. Fix it on the sequence's page first.", await TestDatabase.TitleAsync(problem));
        Assert.Equal(HttpStatusCode.NotFound, (await PickAsync(machine, Guid.NewGuid())).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await PickAsync(machine, sequence.Id, diskNumber: -1)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await PickAsync(machine, sequence.Id, computerName: "PC 0001")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await PickAsync(machine, sequence.Id, computerName: "-PC")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await PickAsync(machine, sequence.Id, computerName: "PC-00000000000001")).StatusCode);

        // Without a domain join, Windows makes a name up.
        Assert.Equal(HttpStatusCode.OK, (await PickAsync(machine, sequence.Id, computerName: null)).StatusCode);
    }

    // Holding the library lock stands in for an image deletion that runs between the lookup and the save.
    [Fact]
    public async Task AChoiceWaitsForTheImageLibrary()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        string operatorName = await application.CreateUserAsync(DdtRoleNames.Operator);
        using DeployingMachine machine = await DeployingMachine.SignedInAsync(application, operatorName);
        Image image = await ImageAsync();
        SequenceView sequence = await SequenceAsync(SequenceRequests.Minimal(image.Id));
        ImageStore store = application.Services.GetRequiredService<ImageStore>();
        Task<HttpResponseMessage> picking;

        await store.LibraryLock.WaitAsync(cancellationToken);

        try
        {
            picking = PickAsync(machine, sequence.Id);
            await Task.WhenAny(picking, Task.Delay(TimeSpan.FromSeconds(1), cancellationToken));
            Assert.False(picking.IsCompleted);

            await application.QueryAsync(database => database.Images.Where(i => i.Id == image.Id).ExecuteDeleteAsync(cancellationToken));
        }
        finally
        {
            store.LibraryLock.Release();
        }

        HttpResponseMessage response = await picking;

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Null((await application.MachineAsync(machine.Id)).ActiveDeploymentId);
    }

    [Fact]
    public async Task TheSignerMustStillBeAllowedToDeploy()
    {
        string lockedOut = await application.CreateUserAsync(DdtRoleNames.Operator);
        string demoted = await application.CreateUserAsync(DdtRoleNames.Operator);
        using DeployingMachine one = await DeployingMachine.SignedInAsync(application, lockedOut);
        using DeployingMachine two = await DeployingMachine.SignedInAsync(application, demoted);

        Assert.True((await one.NextAsync()).CanPickSequence);
        Assert.True((await two.NextAsync()).CanPickSequence);

        await ChangeUserAsync(lockedOut, (users, user) => users.SetLockoutEndDateAsync(user, DateTimeOffset.UtcNow.AddHours(1)));
        await ChangeUserAsync(demoted, async (users, user) =>
        {
            await users.RemoveFromRoleAsync(user, DdtRoleNames.Operator);
            await users.AddToRoleAsync(user, DdtRoleNames.Viewer);
        });

        Assert.False((await one.NextAsync()).CanPickSequence);
        Assert.False((await two.NextAsync()).CanPickSequence);
        Assert.Equal(HttpStatusCode.Forbidden, (await two.Agent.SequencesAsync(two.Id, two.Token)).StatusCode);
    }

    [Fact]
    public async Task AFailedMachineOffersThePickerAgain()
    {
        string operatorName = await application.CreateUserAsync(DdtRoleNames.Operator);
        using DeployingMachine machine = await DeployingMachine.SignedInAsync(application, operatorName);
        SequenceView sequence = await SequenceAsync(SequenceRequests.ScriptOnly());

        AgentRun first = await RegisteredMachine.ReadAsync<AgentRun>(await PickAsync(machine, sequence.Id));
        await application.MoveRunAsync(machine.Id, DeploymentState.Running);
        await application.MoveRunAsync(machine.Id, DeploymentState.Failed, "The script failed.");

        AgentNextResult failed = await machine.NextAsync();

        Assert.Equal(MachineState.Failed, failed.State);
        Assert.True(failed.CanPickSequence);

        AgentRun second = await RegisteredMachine.ReadAsync<AgentRun>(await PickAsync(machine, sequence.Id));

        Assert.NotEqual(first.Id, second.Id);
        Assert.Equal(MachineState.Approved, (await machine.NextAsync()).State);
    }

    // A rule only suggests at the console: the sequence comes first and marked, and choosing it keeps the rule on the
    // run for the history.
    [Fact]
    public async Task TheRulesSequenceIsSuggestedAtTheMachine()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        string operatorName = await application.CreateUserAsync(DdtRoleNames.Operator);
        using DeployingMachine machine = await DeployingMachine.SignedInAsync(application, operatorName);
        SequenceView suggested = await SequenceAsync(SequenceRequests.ScriptOnly());
        SequenceView other = await SequenceAsync(SequenceRequests.ScriptOnly());
        RuleView rule = await administrator.CreatedRuleAsync(RuleRequests.MacRule(suggested.Id, machine.Registration.PrimaryMac));

        AgentNextResult next = await machine.NextAsync();
        IReadOnlyList<AgentSequenceChoice> choices = await ChoicesAsync(machine);

        Assert.Equal(suggested.Id, next.SuggestedSequenceId);
        Assert.True(Assert.Single(choices, c => c.Id == suggested.Id).Suggested);
        Assert.False(Assert.Single(choices, c => c.Id == other.Id).Suggested);

        AgentRun run = await RegisteredMachine.ReadAsync<AgentRun>(await PickAsync(machine, suggested.Id));
        DeploymentView view = await administrator.RunAsync(run.Id);

        Assert.Equal(DeploymentSource.Console, view.Summary.Source);
        Assert.Equal(rule.Id, view.RuleId);

        // Choosing another sequence than the rule's leaves the rule out of the run.
        using DeployingMachine another = await DeployingMachine.SignedInAsync(application, operatorName);
        await administrator.CreatedRuleAsync(RuleRequests.MacRule(suggested.Id, another.Registration.PrimaryMac));
        AgentRun chosen = await RegisteredMachine.ReadAsync<AgentRun>(await PickAsync(another, other.Id));

        Assert.Null((await administrator.RunAsync(chosen.Id)).RuleId);
    }

    // The console could not start a sequence with problems, so the rule that chooses one suggests nothing.
    [Fact]
    public async Task ARulesSequenceWithProblemsIsNotSuggested()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        string operatorName = await application.CreateUserAsync(DdtRoleNames.Operator);
        using DeployingMachine machine = await DeployingMachine.SignedInAsync(application, operatorName);
        SequenceView broken = await SequenceAsync(SequenceRequests.Minimal(Guid.Empty));
        await administrator.CreatedRuleAsync(RuleRequests.MacRule(broken.Id, machine.Registration.PrimaryMac));

        AgentNextResult next = await machine.NextAsync();

        Assert.True(next.CanPickSequence);
        Assert.Null(next.SuggestedSequenceId);
        Assert.DoesNotContain(await ChoicesAsync(machine), c => c.Id == broken.Id || c.Suggested);
    }
}
