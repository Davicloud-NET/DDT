// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Net;
using System.Security.Cryptography;
using DDT.Contracts.Accounts;
using DDT.Contracts.Agents;
using DDT.Contracts.Deployments;
using DDT.Contracts.Sequences;
using DDT.Server.Data;
using DDT.Server.Deployments;
using DDT.Server.Sequences;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;
using Xunit;
using static DDT.Server.Tests.AccountRequests;

namespace DDT.Server.Tests;

// Accounts and the accounts given for a run as PostgreSQL stores them: its unique index on names, its text columns, and
// a share fetched by a running step.
public sealed class PostgresAccountTests
{
    [Fact]
    public async Task StoresAccountsAndHandsAShareToItsStep()
    {
        PostgreSqlContainer? started = await TestPostgres.StartAsync();
        Assert.SkipWhen(started is null, "Docker is not running, so there is no PostgreSQL to test against. Start Docker to run this test.");

        CancellationToken cancellation = TestContext.Current.CancellationToken;
        await using PostgreSqlContainer container = started;
        using PostgresApplication application = new(container.GetConnectionString());
        SignedInClient administrator = await application.AdministratorAsync();
        string proof = await administrator.TokenAsync();

        AccountView account = await administrator.CreatedAccountAsync(Request() with { Name = "Driver share", Hosts = ["files.corp.example", "10.0.0.5"] });
        HttpResponseMessage taken = await administrator.CreateAccountAsync(Request() with { Name = "DRIVER SHARE" }, proof);

        Assert.Equal(HttpStatusCode.BadRequest, taken.StatusCode);
        Assert.Equal(["files.corp.example", "10.0.0.5"], (await administrator.AccountAsync(account.Id)).Hosts);

        using DeployingMachine machine = await DeployingMachine.ApprovedAsync(application, administrator);
        Guid imageId = (await application.SeedImageAsync(RandomNumberGenerator.GetBytes(4096))).Id;
        RunScriptStep script = Script(account.Id);
        SequenceView sequence = await administrator.CreatedSequenceAsync(SequenceRequests.Definition([.. SequenceRequests.Minimal(imageId).Steps, script]) with
        {
            Inputs = [new InputDeclaration { Name = "Given", Label = "Given", Kind = InputKind.Account, Account = new AccountDestination { Hosts = ["files.corp.example"] } }],
        });

        Assert.Equal(sequence.Id, Assert.Single((await administrator.AccountAsync(account.Id)).UsedBy).SequenceId);

        DeploymentSummary assigned = await administrator.AssignedAsync(machine.Id, sequence.Id, "PC-0009");
        AgentRun run = (await machine.NextAsync()).Run!;

        await GiveAsync(application, assigned.Id, cancellation);
        await machine.ReportOkAsync(run.Id, TestReports.Running(
            TestReports.Step(run.Sequence.Steps[0], StepState.Done),
            TestReports.Step(run.Sequence.Steps[1], StepState.Done),
            TestReports.Step(run.Sequence.Steps[2], StepState.Running)));

        HttpResponseMessage response = await machine.Agent.RunAccountsAsync(machine.Id, machine.Token, run.Id, script.Id);
        AgentStepAccounts accounts = await RegisteredMachine.ReadAsync<AgentStepAccounts>(response);

        Assert.Equal(
            [(@"\\files.corp.example\drivers", @"CORP\svc-ddt", Password), (@"\\files.corp.example\given", @"CORP\jane", "Given 7")],
            accounts.Shares.Select(share => (share.Path, share.UserName, share.Password)));
        string subject = run.Id.ToString("D");
        Assert.Equal(2, await application.QueryAsync(database => database.AuditEvents.CountAsync(
            e => e.SubjectId == subject && e.Action == Server.Machines.AuditActions.DeploymentSecretRead,
            cancellation)));

        Assert.Equal(HttpStatusCode.NoContent, (await administrator.DeleteAccountAsync(
            (await administrator.CreatedAccountAsync(Request() with { Name = "Unused" })).Id,
            proof)).StatusCode);
    }

    // One share with the stored account, and one with the account given for the run.
    private static RunScriptStep Script(Guid accountId) => new()
    {
        Id = Guid.NewGuid(),
        Name = "Copy drivers",
        Phase = SequencePhase.WindowsPE,
        Interpreter = ScriptInterpreter.Cmd,
        Script = "echo copied",
        Shares =
        [
            new ShareConnection(@"\\files.corp.example\drivers", new AccountReference(accountId, null)),
            new ShareConnection(@"\\files.corp.example\given", new AccountReference(null, "Given")),
        ],
    };

    // As a technician gives it at the machine, with a NUL in the name that PostgreSQL could not store.
    private static async Task GiveAsync(PostgresApplication application, Guid runId, CancellationToken cancellation)
    {
        using IServiceScope scope = application.Services.CreateScope();
        DdtDbContext database = scope.ServiceProvider.GetRequiredService<DdtDbContext>();
        Deployment deployment = await database.Deployments.SingleAsync(d => d.Id == runId, cancellation);
        InputDeclaration input = SequenceDocuments.Read((await database.DeploymentSnapshots.SingleAsync(s => s.DeploymentId == runId, cancellation)).Definition)
            .Inputs!.Single();

        Assert.Null(await scope.ServiceProvider.GetRequiredService<RunCredentials>().KeepAsync(
            deployment,
            input,
            new InputAnswer("Given", null, @"CORP\jane", "Given 7"),
            new RunCredentialGiver(null, "technician\0 with a NUL", AtMachine: true),
            cancellation));
        await database.SaveChangesAsync(cancellation);
    }
}
