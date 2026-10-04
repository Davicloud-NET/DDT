// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Net;
using System.Security.Cryptography;
using DDT.Contracts.Agents;
using DDT.Contracts.Deployments;
using DDT.Contracts.Images;
using DDT.Contracts.Machines;
using DDT.Contracts.Packages;
using DDT.Contracts.Rules;
using DDT.Contracts.Sequences;
using DDT.Contracts.Values;
using DDT.Server.Data;
using DDT.Server.Deployments;
using DDT.Server.Images;
using DDT.Server.Machines;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DDT.Server.Tests;

// A database server refuses what SQLite accepts: a value longer than its column and, on PostgreSQL, a NUL and a
// DateTimeOffset that is not UTC. SQL Server has its own first migration, with rules of its own for deleting.
public sealed class DatabaseServerDeploymentTests
{
    [Theory]
    [InlineData(DatabaseProvider.PostgreSql)]
    [InlineData(DatabaseProvider.SqlServer)]
    public async Task AppliesTheMigrationsAndRunsADeployment(DatabaseProvider provider)
    {
        await using TestDatabaseServer server = await TestDatabaseServer.StartAsync(provider);
        using DatabaseServerApplication application = new(server);
        SignedInClient administrator = await application.AdministratorAsync();

        // DdtApplication sets an empty connection string, which means SQLite. Without this check, losing the
        // override would pass on SQLite and leave the migrations untested.
        Assert.Equal(
            provider == DatabaseProvider.SqlServer ? "Microsoft.EntityFrameworkCore.SqlServer" : "Npgsql.EntityFrameworkCore.PostgreSQL",
            await application.QueryAsync(database => Task.FromResult(database.Database.ProviderName)));

        using DeployingMachine machine = await DeployingMachine.ApprovedAsync(
            application,
            administrator,
            [DeployingMachine.Disk(0, "Disk\0 with a NUL and a model name far longer than the sixty four characters its line keeps")]);
        Image image = await application.SeedImageAsync(RandomNumberGenerator.GetBytes(4096));
        SequenceView sequence = await administrator.CreatedSequenceAsync(SequenceRequests.Definition(
        [
            .. SequenceRequests.Minimal(image.Id).Steps,
            new RebootStep { Id = Guid.NewGuid(), Name = "A NUL\0 in a step" },
        ]));

        DeploymentSummary run = await AssignedAsync(administrator, machine, sequence, image);

        // An account given for the run lives until the run ends.
        await application.QueryAsync(database =>
        {
            database.RunCredentials.Add(Credential(run.Id));

            return database.SaveChangesAsync(TestContext.Current.CancellationToken);
        });

        AgentRun handed = (await machine.NextAsync()).Run!;
        await machine.ReportOkAsync(handed.Id, TestReports.Running(TestReports.Step(handed.Sequence.Steps[0], StepState.Running)));
        Assert.Equal(1, await CredentialsAsync(application, run.Id));
        await machine.ReportOkAsync(handed.Id, TestReports.Report(
            DeploymentState.Done,
            [.. handed.Sequence.Steps.Select(step => TestReports.Step(step, StepState.Done, "A NUL\0 in a note"))]));

        DeploymentView done = await administrator.RunAsync(run.Id);

        Assert.Equal(DeploymentState.Done, done.Summary.State);
        Assert.Equal(0, await CredentialsAsync(application, run.Id));
        Assert.All(done.Steps, step => Assert.Equal("A NUL in a note", step.Error));
        Assert.Equal(TimeSpan.Zero, done.Steps[0].StartedUtc?.Offset);
        Assert.Equal(MachineState.Done, Assert.Single(
            await RegisteredMachine.ReadAsync<IReadOnlyList<MachineSummary>>(await administrator.GetAsync("/api/machines")),
            m => m.Id == machine.Id).State);

        await AbandonedAsync(application, administrator, sequence);
    }

    // Assigns the sequence and checks the run's rows and its machine's row as the database stored them.
    private static async Task<DeploymentSummary> AssignedAsync(SignedInClient administrator, DeployingMachine machine, SequenceView sequence, Image image)
    {
        DeploymentSummary run = await administrator.AssignedAsync(machine.Id, sequence.Id, "PC-0006");
        DeploymentView view = await administrator.RunAsync(run.Id);

        Assert.Equal("A NUL in a step", view.Steps[2].Name);
        Assert.Equal(image.Sha256, Assert.Single(view.Artifacts).Sha256);

        IReadOnlyList<MachineSummary> machines = await RegisteredMachine.ReadAsync<IReadOnlyList<MachineSummary>>(
            await administrator.GetAsync("/api/machines"));
        MachineSummary assigned = Assert.Single(machines, m => m.Id == machine.Id);

        Assert.Equal(DeploymentState.Assigned, assigned.Deployment?.State);
        Assert.Equal("PC-0006", assigned.AssignedName);
        Assert.DoesNotContain('\0', assigned.Disks!);
        Assert.Equal(
            [run.Id],
            (await RegisteredMachine.ReadAsync<IReadOnlyList<DeploymentSummary>>(await administrator.GetAsync($"/api/machines/{machine.Id}/deployments"))).Select(r => r.Id));

        return run;
    }

    // A run whose agent is gone for good fails. An account stored for it is deleted at the next start.
    private static async Task AbandonedAsync(DatabaseServerApplication application, SignedInClient administrator, SequenceView sequence)
    {
        using DeployingMachine silent = await DeployingMachine.ApprovedAsync(application, administrator);
        await administrator.AssignedAsync(silent.Id, sequence.Id);
        AgentRun abandoned = (await silent.NextAsync()).Run!;
        await silent.ReportOkAsync(abandoned.Id, TestReports.Running(TestReports.Step(abandoned.Sequence.Steps[0], StepState.Running)));
        await application.ChangeMachineAsync(silent.Id, m => m.LastSeenUtc = DateTimeOffset.UtcNow - MachineTokenLifetimes.Run - TimeSpan.FromMinutes(1));

        Assert.Equal(1, await application.Services.GetRequiredService<AbandonedRunSweeper>().SweepOnceAsync(TestContext.Current.CancellationToken));
        Assert.Equal(DeploymentState.Failed, (await administrator.RunAsync(abandoned.Id)).Summary.State);

        // An account stored for a run that's over, as a change by hand could leave it, is deleted at the next start.
        await application.QueryAsync(database =>
        {
            database.RunCredentials.Add(Credential(abandoned.Id));

            return database.SaveChangesAsync(TestContext.Current.CancellationToken);
        });

        Assert.Equal(1, await application.Services.GetRequiredService<RunCredentialSweeper>().SweepOnceAsync(TestContext.Current.CancellationToken));
        Assert.Equal(0, await CredentialsAsync(application, abandoned.Id));
    }

    // Covers a tree's rows, the answers the run waits for (compared and replaced in one statement), its values and
    // variables as JSON, and a pause. Each has a NUL wherever the agent or a person could put one.
    [Theory]
    [InlineData(DatabaseProvider.PostgreSql)]
    [InlineData(DatabaseProvider.SqlServer)]
    public async Task RunsATreeThatWaitsForAnswersAndPauses(DatabaseProvider provider)
    {
        await using TestDatabaseServer server = await TestDatabaseServer.StartAsync(provider);
        using DatabaseServerApplication application = new(server);
        SignedInClient administrator = await application.AdministratorAsync();
        using DeployingMachine machine = await DeployingMachine.ApprovedAsync(application, administrator);
        RunScriptStep before = TreeSequences.Script("Before");
        PauseStep pause = TreeSequences.Pause();
        GroupStep group = TreeSequences.Group("Then", pause, TreeSequences.Script("After"));
        SequenceView sequence = await administrator.CreatedSequenceAsync(SequenceRequests.Definition(before, group) with
        {
            Variables = [new VariableDeclaration { Name = "Office", SetBySteps = true }],
            Inputs = [new InputDeclaration { Name = "Room", Label = "Room", Required = true, AskAt = InputAsk.Machine }],
        });
        Guid runId = (await administrator.AssignedAsync(machine.Id, sequence.Id)).Id;
        AgentRun run = (await machine.NextAsync()).Run!;

        Assert.Equal("Room", Assert.Single(run.PendingInputs!).Name);
        await machine.ReportOkAsync(runId, TestReports.Report(DeploymentState.Running, []) with { Activity = RunActivity.WaitingForInput });
        DeploymentView answered = await RegisteredMachine.ReadAsync<DeploymentView>(
            await administrator.AnswerAsync(machine.Id, new InputAnswer("Room", "A 1")));
        Assert.False(answered.Summary.Waiting);
        Assert.Equal(HttpStatusCode.Conflict, (await administrator.AnswerAsync(machine.Id, new InputAnswer("Room", "B 2"))).StatusCode);

        await machine.ReportOkAsync(runId, TestReports.Report(DeploymentState.Running, []) with { Activity = RunActivity.WaitingForInput });
        Assert.Equal("A 1", machine.LastReported!.Values!["Room"]);

        StepRunState[] paused =
        [
            TreeSequences.Visit(before, StepState.Done) with { Evaluation = [new TestEvaluation("when\0", true, "A NUL\0 here")] },
            TreeSequences.Visit(group, StepState.Running),
            TreeSequences.Visit(pause, StepState.Running),
        ];
        await machine.ReportOkAsync(runId, TestReports.Report(DeploymentState.Running, paused) with
        {
            Activity = RunActivity.Paused,
            PauseMessage = "Check\0 the BIOS.",
            Variables = new Dictionary<string, string> { ["Office"] = "Pro\0Plus" },
        });

        DeploymentView waiting = await administrator.RunAsync(runId);
        Assert.Equal(("Check the BIOS.", "ProPlus"), (waiting.Pause!.Message, waiting.Variables!["Office"]));
        Assert.Equal(new TestEvaluation("when", true, "A NUL here"), Assert.Single(waiting.Steps[0].Evaluation!));
        Assert.Equal([(Guid?)null, null, group.Id, group.Id], waiting.Steps.Select(step => step.ParentId));

        (await administrator.PostAsync($"/api/machines/{machine.Id}/deployments/current/continue", new ContinueRunRequest(pause.Id, 1))).EnsureSuccessStatusCode();
        await machine.ReportOkAsync(runId, TestReports.Report(DeploymentState.Running, paused) with { Activity = RunActivity.Paused });
        Assert.Equal(pause.Id, machine.LastReported!.ContinueStepId);

        await machine.ReportOkAsync(runId, TestReports.Report(
            DeploymentState.Done,
            [.. SequenceTree.Nodes(run.Sequence).Select(node => TreeSequences.Visit(node, StepState.Done))]));
        Assert.Equal(DeploymentState.Done, (await administrator.RunAsync(runId)).Summary.State);
    }

    private static RunCredential Credential(Guid runId) => new()
    {
        DeploymentId = runId,
        InputName = "JoinAccount",
        UserName = @"CORP\alice",
        ProtectedPassword = "ciphertext",
        Hosts = """["files.corp.example.com"]""",
        CreatedUtc = DateTimeOffset.UtcNow,
    };

    private static Task<int> CredentialsAsync(DatabaseServerApplication application, Guid runId) =>
        application.QueryAsync(database => database.RunCredentials.CountAsync(c => c.DeploymentId == runId, TestContext.Current.CancellationToken));

    [Theory]
    [InlineData(DatabaseProvider.PostgreSql)]
    [InlineData(DatabaseProvider.SqlServer)]
    public async Task StoresTheLibraryOfSequencesPackagesAndRules(DatabaseProvider provider)
    {
        await using TestDatabaseServer server = await TestDatabaseServer.StartAsync(provider);
        using DatabaseServerApplication application = new(server);
        SignedInClient administrator = await application.AdministratorAsync();
        Image image = await application.SeedImageAsync(RandomNumberGenerator.GetBytes(4096));

        SequenceView saved = await SavedSequenceAsync(administrator, image);
        await TargetedPackageAsync(application, administrator);

        MachineRoleView role = await administrator.CreatedRoleAsync(new SaveMachineRoleRequest(0, "Finance\0 laptops", "A NUL\0 here", [new NamedValue("Office", "Vienna\0")]));
        RuleView other = await administrator.CreatedRuleAsync(RuleRequests.MacRule(saved.Id, RuleRequests.RandomMac()));
        RuleView rule = await administrator.CreatedRuleAsync(RuleRequests.ModelRule(saved.Id, "Latitude 5440", "Dell Inc.") with
        {
            Name = "Dell\0 laptops",
            Values = [new NamedValue("ComputerName", "PC-{{SerialNumber|alnum}}\0")],
            RoleIds = [role.Id],
        });
        RuleView last = await administrator.CreatedRuleAsync(RuleRequests.ModelRule(saved.Id, "LATITUDE 5440", "dell inc."));
        using RegisteredMachine machine = await application.RegisterModelAsync("DELL INC.", "latitude 5440");

        Assert.Equal(("Finance laptops", "Vienna"), (role.Name, role.Values[0].Value));
        Assert.Equal(("Dell laptops", "PC-{{SerialNumber|alnum}}"), (rule.Name, rule.Values[0].Value));
        MachineSequenceResolution resolution = await administrator.ResolutionAsync(machine.Id);
        Assert.Equal(rule.Id, resolution.RuleId);
        Assert.Equal([rule.Id, last.Id], resolution.MatchedRuleIds);
        Assert.Contains(new ResolvedValue("ComputerName", "PC-00000000", ValueSource.Rule, rule.Id, rule.Name, false), resolution.Values!);
        Assert.Contains(new ResolvedValue("Office", "Vienna", ValueSource.Role, role.Id, role.Name, false), resolution.Values!);

        // Places are unique. So a reorder and a delete move rules through places no rule has, in one transaction.
        IReadOnlyList<RuleView> rules = await administrator.RulesAsync();
        IReadOnlyList<RuleView> reordered = await RegisteredMachine.ReadAsync<IReadOnlyList<RuleView>>(
            await administrator.ReorderAsync([.. rules.Select(r => r.Id).Reverse()]));
        Assert.Equal(rules.Select(r => r.Id).Reverse(), reordered.Select(r => r.Id));
        Assert.Equal(last.Id, (await administrator.ResolutionAsync(machine.Id)).RuleId);

        IReadOnlyList<RuleView> left = await RegisteredMachine.ReadAsync<IReadOnlyList<RuleView>>(await administrator.DeleteAsync($"{RuleRequests.Rules}/{last.Id}"));
        Assert.Equal([rule.Id, other.Id], left.Select(r => r.Id));
        Assert.Equal([0, 1], left.Select(r => r.Position));

        Assert.Equal(HttpStatusCode.Conflict, (await administrator.DeleteAsync($"{RuleRequests.Roles}/{role.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await administrator.DeleteAsync($"{SequenceRequests.Sequences}/{saved.Id}")).StatusCode);
    }

    // Creates a sequence, then saves it under a new name with a new step.
    // Saves with a taken name or a stale revision are refused.
    private static async Task<SequenceView> SavedSequenceAsync(SignedInClient administrator, Image image)
    {
        SequenceView created = await RegisteredMachine.ReadAsync<SequenceView>(await administrator.PostAsync(
            SequenceRequests.Sequences,
            new CreateSequenceRequest("Install", "A NUL\0 in the description", SequenceRequests.Minimal(image.Id))));
        SequenceView saved = await RegisteredMachine.ReadAsync<SequenceView>(await administrator.SaveSequenceAsync(
            created,
            created.Definition with { Steps = [.. created.Definition.Steps, new RebootStep { Id = Guid.NewGuid(), Name = "A NUL\0 in a step" }] },
            "Install Windows"));

        Assert.Equal("A NUL in the description", saved.Description);
        Assert.Equal(2, saved.Revision);
        Assert.Empty(saved.Problems);
        Assert.Equal(HttpStatusCode.BadRequest, (await administrator.CreateSequenceAsync(saved.Definition, "INSTALL WINDOWS")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await administrator.SaveSequenceAsync(created)).StatusCode);

        return saved;
    }

    // A driver package for a model. A run it's frozen into keeps it from being deleted.
    private static async Task TargetedPackageAsync(DatabaseServerApplication application, SignedInClient administrator)
    {
        PackageSummary package = await administrator.UploadedPackageAsync(PackageRequests.DriverZip(), UploadKind.Drivers);
        PackageSummary targeted = await RegisteredMachine.ReadAsync<PackageSummary>(await administrator.PutAsync(
            $"{PackageRequests.Packages}/{package.Id}",
            new UpdatePackageRequest("Latitude", "A NUL\0 here too", [new HardwareModel("Dell Inc.", "Latitude 5440")])));

        Assert.Equal("A NUL here too", targeted.Description);
        Assert.Equal(new HardwareModel("Dell Inc.", "Latitude 5440"), Assert.Single(targeted.Targets));

        await application.AddAssignedRunAsync(ArtifactKind.Drivers, package.Id, package.Sha256);
        Assert.Equal(HttpStatusCode.Conflict, (await administrator.DeleteAsync($"{PackageRequests.Packages}/{package.Id}")).StatusCode);
    }
}
