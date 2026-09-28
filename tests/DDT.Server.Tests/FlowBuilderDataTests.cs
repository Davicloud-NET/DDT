// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Text.Json;
using DDT.Contracts;
using DDT.Contracts.Deployments;
using DDT.Contracts.Sequences;
using DDT.Contracts.Values;
using DDT.Server.Accounts;
using DDT.Server.Data;
using DDT.Server.Deployments;
using DDT.Server.Machines;
using DDT.Server.Rules;
using DDT.Server.Sequences;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using DeploymentStep = DDT.Server.Deployments.DeploymentStep;

namespace DDT.Server.Tests;

// What the flow builder stores: rules, machine roles, accounts, a run's credentials, and a run's tree, values and pause.
public sealed class FlowBuilderDataTests(DdtApplication application) : IClassFixture<DdtApplication>
{
    private static readonly DateTimeOffset s_now = new(2026, 9, 28, 8, 0, 0, TimeSpan.Zero);

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private async Task<T> InScopeAsync<T>(Func<DdtDbContext, Task<T>> work)
    {
        await using AsyncServiceScope scope = application.Services.CreateAsyncScope();

        return await work(scope.ServiceProvider.GetRequiredService<DdtDbContext>());
    }

    private Task InScopeAsync(Func<DdtDbContext, Task> work) => InScopeAsync(async database =>
    {
        await work(database);

        return true;
    });

    private async Task<Guid> SequenceAsync()
    {
        string name = $"Sequence {Guid.NewGuid():N}";
        TaskSequence sequence = new()
        {
            Id = Guid.CreateVersion7(),
            Name = name,
            NormalizedName = name.ToUpperInvariant(),
            Definition = SequenceDocuments.Write(new SequenceDefinition(1, [])),
            Revision = 1,
            CreatedUtc = s_now,
            UpdatedUtc = s_now,
        };

        await InScopeAsync(database =>
        {
            database.TaskSequences.Add(sequence);

            return database.SaveChangesAsync(Cancellation);
        });

        return sequence.Id;
    }

    private async Task<Guid> RunAsync(DeploymentState state = DeploymentState.Running)
    {
        Guid machineId = Guid.CreateVersion7();
        Guid runId = Guid.CreateVersion7();
        string mac = "02" + Convert.ToHexString(Guid.NewGuid().ToByteArray(), 0, 5);

        await InScopeAsync(database =>
        {
            database.Machines.Add(new Machine
            {
                Id = machineId,
                SmbiosUuid = Guid.NewGuid().ToString("D"),
                PrimaryMac = mac,
                MacAddresses = mac,
                FirstSeenUtc = s_now,
                LastSeenUtc = s_now,
                ActiveDeploymentId = runId,
            });
            database.Deployments.Add(new Deployment
            {
                Id = runId,
                MachineId = machineId,
                Title = "A run",
                State = state,
                CreatedUtc = s_now,
                UpdatedUtc = s_now,
            });

            return database.SaveChangesAsync(Cancellation);
        });

        return runId;
    }

    private static Rule NewRule(int position, Guid? sequenceId = null) => new()
    {
        Id = Guid.CreateVersion7(),
        Position = position,
        Name = $"Rule {position}",
        Enabled = true,
        TaskSequenceId = sequenceId,
        Revision = 1,
        CreatedUtc = s_now,
        UpdatedUtc = s_now,
    };

    // A rule is read back through the contracts, the same way the rules page and the resolver read it.
    [Fact]
    public async Task KeepsARuleAsTheContractsWriteIt()
    {
        Guid sequenceId = await SequenceAsync();
        ConditionNode when = new AllCondition
        {
            Parts =
            [
                new TestCondition(MachineVariableNames.Manufacturer, ConditionOperator.Equals, "LENOVO"),
                new TestCondition(MachineVariableNames.Model, ConditionOperator.Matches, "21HD*"),
            ],
        };
        IReadOnlyList<NamedValue> values = [new("ComputerName", "PC-{{SerialNumber|alnum|right:12}}")];
        IReadOnlyList<Guid> roleIds = [Guid.NewGuid()];
        Rule rule = NewRule(10_000, sequenceId);
        rule.When = JsonSerializer.Serialize(when, DdtJsonContext.Default.ConditionNode);
        rule.Values = JsonSerializer.Serialize(values, DdtJsonContext.Default.IReadOnlyListNamedValue);
        rule.RoleIds = JsonSerializer.Serialize(roleIds, DdtJsonContext.Default.IReadOnlyListGuid);

        await InScopeAsync(database =>
        {
            database.Rules.Add(rule);

            return database.SaveChangesAsync(Cancellation);
        });

        Rule stored = await InScopeAsync(database => database.Rules.AsNoTracking().SingleAsync(r => r.Id == rule.Id, Cancellation));

        AllCondition read = Assert.IsType<AllCondition>(JsonSerializer.Deserialize(stored.When!, DdtJsonContext.Default.ConditionNode));
        Assert.Equal(((AllCondition)when).Parts, read.Parts);
        Assert.Equal(values, JsonSerializer.Deserialize(stored.Values, DdtJsonContext.Default.IReadOnlyListNamedValue));
        Assert.Equal(roleIds, JsonSerializer.Deserialize(stored.RoleIds, DdtJsonContext.Default.IReadOnlyListGuid));
        Assert.Equal((sequenceId, true, 1L), (stored.TaskSequenceId, stored.Enabled, stored.Revision));
    }

    [Fact]
    public async Task TwoRulesNeverShareAPlace()
    {
        await InScopeAsync(database =>
        {
            database.Rules.Add(NewRule(20_000));

            return database.SaveChangesAsync(Cancellation);
        });

        await Assert.ThrowsAsync<DbUpdateException>(() => InScopeAsync(database =>
        {
            database.Rules.Add(NewRule(20_000));

            return database.SaveChangesAsync(Cancellation);
        }));
    }

    // Swapping two rules takes two saves. The index is checked row by row, so the first save moves them out of the way.
    [Fact]
    public async Task RulesSwapPlacesInTwoSaves()
    {
        Rule first = NewRule(30_000);
        Rule second = NewRule(30_001);

        await InScopeAsync(database =>
        {
            database.Rules.AddRange(first, second);

            return database.SaveChangesAsync(Cancellation);
        });

        await InScopeAsync(async database =>
        {
            await using IDbContextTransaction transaction = await database.Database.BeginTransactionAsync(Cancellation);
            List<Rule> rules = await database.Rules.Where(r => r.Id == first.Id || r.Id == second.Id).ToListAsync(Cancellation);
            Rule top = rules.Single(r => r.Id == first.Id);
            Rule next = rules.Single(r => r.Id == second.Id);

            top.Position = -1 - top.Position;
            next.Position = -1 - next.Position;
            await database.SaveChangesAsync(Cancellation);

            top.Position = 30_001;
            next.Position = 30_000;
            await database.SaveChangesAsync(Cancellation);
            await transaction.CommitAsync(Cancellation);
        });

        Dictionary<Guid, int> positions = await InScopeAsync(database => database.Rules
            .Where(r => r.Id == first.Id || r.Id == second.Id)
            .ToDictionaryAsync(r => r.Id, r => r.Position, Cancellation));

        Assert.Equal(30_001, positions[first.Id]);
        Assert.Equal(30_000, positions[second.Id]);
    }

    [Fact]
    public async Task ASequenceThatARuleChoosesCannotBeDeleted()
    {
        Guid sequenceId = await SequenceAsync();

        await InScopeAsync(database =>
        {
            database.Rules.Add(NewRule(40_000, sequenceId));

            return database.SaveChangesAsync(Cancellation);
        });

        await Assert.ThrowsAsync<DbUpdateException>(() => InScopeAsync(async database =>
        {
            database.TaskSequences.Remove(await database.TaskSequences.SingleAsync(s => s.Id == sequenceId, Cancellation));
            await database.SaveChangesAsync(Cancellation);
        }));
    }

    [Fact]
    public async Task MachineRolesAndAccountsDifferInMoreThanCase()
    {
        string name = $"Kiosk {Guid.NewGuid():N}";

        MachineRole Role(string roleName) => new()
        {
            Id = Guid.CreateVersion7(),
            Name = roleName,
            NormalizedName = roleName.Trim().ToUpperInvariant(),
            Revision = 1,
            CreatedUtc = s_now,
            UpdatedUtc = s_now,
        };

        Account Account(string accountName) => new()
        {
            Id = Guid.CreateVersion7(),
            Name = accountName,
            NormalizedName = accountName.Trim().ToUpperInvariant(),
            UserName = @"CORP\svc-deploy",
            Revision = 1,
            CreatedUtc = s_now,
            UpdatedUtc = s_now,
        };

        await InScopeAsync(database =>
        {
            database.MachineRoles.Add(Role(name));
            database.Accounts.Add(Account(name));

            return database.SaveChangesAsync(Cancellation);
        });

        await Assert.ThrowsAsync<DbUpdateException>(() => InScopeAsync(database =>
        {
            database.MachineRoles.Add(Role(name.ToLowerInvariant()));

            return database.SaveChangesAsync(Cancellation);
        }));
        await Assert.ThrowsAsync<DbUpdateException>(() => InScopeAsync(database =>
        {
            database.Accounts.Add(Account(name.ToLowerInvariant()));

            return database.SaveChangesAsync(Cancellation);
        }));

        MachineRole role = await InScopeAsync(database => database.MachineRoles.AsNoTracking().SingleAsync(r => r.Name == name, Cancellation));
        Account account = await InScopeAsync(database => database.Accounts.AsNoTracking().SingleAsync(a => a.Name == name, Cancellation));

        Assert.Equal(("[]", "[]", (string?)null), (role.Values, account.Hosts, account.ProtectedPassword));
    }

    // Removing a machine with its runs removes their credentials too, in the database itself.
    [Fact]
    public async Task ARunsCredentialsGoWithItsMachine()
    {
        Guid runId = await RunAsync();
        Guid machineId = await InScopeAsync(database => database.Deployments.Where(d => d.Id == runId).Select(d => d.MachineId).SingleAsync(Cancellation));

        await InScopeAsync(database =>
        {
            database.RunCredentials.Add(new RunCredential
            {
                DeploymentId = runId,
                InputName = "JoinAccount",
                UserName = @"CORP\alice",
                ProtectedPassword = "ciphertext",
                Domain = "corp.example.com",
                ProvidedAtMachine = true,
                CreatedUtc = s_now,
            });

            return database.SaveChangesAsync(Cancellation);
        });

        Assert.Equal(1, await InScopeAsync(database => database.RunCredentials.CountAsync(c => c.DeploymentId == runId, Cancellation)));

        await InScopeAsync(database => database.Machines.Where(m => m.Id == machineId).ExecuteDeleteAsync(Cancellation));

        Assert.Equal(0, await InScopeAsync(database => database.RunCredentials.CountAsync(c => c.DeploymentId == runId, Cancellation)));
    }

    // Steps without node fields, like the server wrote them before trees, still read the same.
    // A step in a tree keeps its node fields.
    [Fact]
    public async Task ARunKeepsItsTreeValuesAndPause()
    {
        Guid runId = await RunAsync();
        Guid parentId = Guid.NewGuid();
        Guid pauseId = Guid.NewGuid();
        IReadOnlyList<TestEvaluation> evaluation = [new("test.parts[0]", true, "Latitude 7440")];
        IReadOnlyList<ResolvedValue> values = [new("ComputerName", "PC-0042", ValueSource.Rule, Guid.NewGuid(), "Laptops", false)];
        IReadOnlyList<RunAnswer> answers = [new("Office", "Vienna", "alice", false, s_now)];

        await InScopeAsync(async database =>
        {
            Deployment run = await database.Deployments.SingleAsync(d => d.Id == runId, Cancellation);
            run.Answers = RunAnswer.Write(answers);
            run.Values = JsonSerializer.Serialize(values, DdtJsonContext.Default.IReadOnlyListResolvedValue);
            run.Variables = JsonSerializer.Serialize(
                new Dictionary<string, string> { ["Office"] = "Vienna" },
                DdtJsonContext.Default.IReadOnlyDictionaryStringString);
            run.PauseStepId = pauseId;
            run.PausePass = 2;
            run.PauseMessage = "Plug in the dock.";
            database.DeploymentSteps.AddRange(
                new DeploymentStep { DeploymentId = runId, StepId = parentId, Index = 0, Name = "If a Latitude", Kind = "if", State = StepState.Running, Pass = 1 },
                new DeploymentStep
                {
                    DeploymentId = runId,
                    StepId = pauseId,
                    Index = 1,
                    Name = "Wait",
                    Kind = "pause",
                    State = StepState.Running,
                    ParentId = parentId,
                    Depth = 1,
                    Pass = 2,
                    Iteration = 3,
                    Branch = IfBranch.Else,
                    Evaluation = JsonSerializer.Serialize(evaluation, DdtJsonContext.Default.IReadOnlyListTestEvaluation),
                });
            await database.SaveChangesAsync(Cancellation);
        });

        (Deployment stored, List<DeploymentStep> steps) = await InScopeAsync(async database => (
            await database.Deployments.AsNoTracking().SingleAsync(d => d.Id == runId, Cancellation),
            await database.DeploymentSteps.AsNoTracking().Where(s => s.DeploymentId == runId).OrderBy(s => s.Index).ToListAsync(Cancellation)));

        Assert.Equal(answers, RunAnswer.Read(stored.Answers));
        Assert.Equal(values, JsonSerializer.Deserialize(stored.Values!, DdtJsonContext.Default.IReadOnlyListResolvedValue));
        Assert.Equal("Vienna", JsonSerializer.Deserialize(stored.Variables!, DdtJsonContext.Default.IReadOnlyDictionaryStringString)!["Office"]);
        Assert.Equal((pauseId, 2, "Plug in the dock.", false), (stored.PauseStepId, stored.PausePass, stored.PauseMessage, stored.InputsPending));
        Assert.Equal(((Guid?)null, 0, 1, 0, (IfBranch?)null), Node(steps[0]));
        Assert.Null(steps[0].Evaluation);
        Assert.Equal(((Guid?)parentId, 1, 2, 3, (IfBranch?)IfBranch.Else), Node(steps[1]));
        Assert.Equal(evaluation, JsonSerializer.Deserialize(steps[1].Evaluation!, DdtJsonContext.Default.IReadOnlyListTestEvaluation));

        // The branch is stored by name, like every other enum of a run.
        string branch = await InScopeAsync(database => database.Database
            .SqlQuery<string>($"SELECT \"Branch\" AS \"Value\" FROM \"DeploymentSteps\" WHERE \"StepId\" = {pauseId}")
            .SingleAsync(Cancellation));
        Assert.Equal("Else", branch);
    }

    private static (Guid? ParentId, int Depth, int Pass, int Iteration, IfBranch? Branch) Node(DeploymentStep step) =>
        (step.ParentId, step.Depth, step.Pass, step.Iteration, step.Branch);
}
