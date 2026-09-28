// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Agent.Deployment;
using DDT.Agent.Sequences;
using DDT.Contracts.Agents;
using DDT.Contracts.Sequences;
using DDT.Core.Sequences;
using Xunit;

namespace DDT.Agent.Tests;

// The share and run-as behaviour around a step, driven through the fakes, so no real logon or share is touched.
public sealed class StepAccountsTests
{
    private const string SharePassword = "Sh4re-never-logged";
    private const string RunAsPassword = "Run4s-never-logged";

    private static readonly AgentShareConnection s_drivers = new(@"\\files.corp.example\drivers", @"CORP\svc-drivers", SharePassword);

    private static RunScriptStep Script(SequencePhase phase, IReadOnlyList<ShareConnection>? shares = null, AccountReference? runAs = null) =>
        TestRuns.Script(5, phase) with
        {
            Shares = shares,
            RunAs = runAs,
        };

    private static ShareConnection Share => new(s_drivers.Path, new AccountReference(Guid.NewGuid(), null));

    [Fact]
    public async Task ConnectsTheSharesBeforeTheStepAndDisconnectsThemAfter()
    {
        RunScriptStep step = Script(SequencePhase.WindowsPE, [Share]);
        using StepRunnerFixture run = new([step]);
        run.Server.OnRunStepAccounts(_ => new AgentStepAccounts(null, [s_drivers]));
        IReadOnlyList<string>? whileRunning = null;
        run.ToolRunner.AnswerExitCode = (_, _, _) =>
        {
            whileRunning = run.Accounts.Shares.Connected;

            return 0;
        };

        StepResult result = await run.Steps.RunAsync(step, run.Context(), TestContext.Current.CancellationToken);

        Assert.Equal(StepOutcome.Done, result.Outcome);
        Assert.Equal([s_drivers.Path], whileRunning);
        Assert.Empty(run.Accounts.Shares.Connected);
        Assert.Equal(
            ["run-report Running session", $"run-accounts {step.Id} session"],
            run.Server.Calls);
        Assert.Equal(
            [$@"connect {s_drivers.Path} as CORP\svc-drivers for the agent", $"disconnect {s_drivers.Path}"],
            run.Accounts.Events);
    }

    [Fact]
    public async Task DisconnectsTheSharesWhenTheStepFails()
    {
        RunScriptStep step = Script(SequencePhase.WindowsPE, [Share]);
        using StepRunnerFixture run = new([step]);
        run.Server.OnRunStepAccounts(_ => new AgentStepAccounts(null, [s_drivers]));
        run.ToolRunner.AnswerExitCode = (_, _, _) => 5;

        StepResult result = await run.Steps.RunAsync(step, run.Context(), TestContext.Current.CancellationToken);

        Assert.Equal(StepOutcome.Failed, result.Outcome);
        Assert.Empty(run.Accounts.Shares.Connected);
        Assert.Contains($"disconnect {s_drivers.Path}", run.Accounts.Events);
    }

    [Fact]
    public async Task DisconnectsTheSharesWhenTheStepThrows()
    {
        RunScriptStep step = Script(SequencePhase.WindowsPE, [Share]);
        using StepRunnerFixture run = new([step]);
        run.Server.OnRunStepAccounts(_ => new AgentStepAccounts(null, [s_drivers]));
        run.ToolRunner.AnswerExitCode = (_, _, _) => throw new DeploymentStepException("The script blew up.");

        StepResult result = await run.Steps.RunAsync(step, run.Context(), TestContext.Current.CancellationToken);

        Assert.Equal(StepOutcome.Failed, result.Outcome);
        Assert.Empty(run.Accounts.Shares.Connected);
        Assert.Contains($"disconnect {s_drivers.Path}", run.Accounts.Events);
    }

    [Fact]
    public async Task AShareThatCannotConnectFailsTheStepWithACleanMessageAndRunsNoScript()
    {
        RunScriptStep step = Script(SequencePhase.WindowsPE, [Share]);
        using StepRunnerFixture run = new([step]);
        run.Server.OnRunStepAccounts(_ => new AgentStepAccounts(null, [s_drivers]));
        run.Accounts.Shares.FailPath = s_drivers.Path;
        run.Accounts.Shares.Failure = new DeploymentStepException($"{s_drivers.Path} could not be connected (error 5): Access is denied.");

        StepResult result = await run.Steps.RunAsync(step, run.Context(), TestContext.Current.CancellationToken);

        Assert.Equal(StepOutcome.Failed, result.Outcome);
        Assert.Contains(s_drivers.Path, result.Error!, StringComparison.Ordinal);
        Assert.DoesNotContain(SharePassword, result.Error!, StringComparison.Ordinal);
        Assert.Empty(run.ToolRunner.Calls);
    }

    [Fact]
    public async Task RunsAScriptAsTheAccountInWindowsAndSignsItOutAfter()
    {
        AccountReference reference = new(Guid.NewGuid(), null);
        RunScriptStep step = Script(SequencePhase.Windows, runAs: reference);
        using StepRunnerFixture run = new([step]);
        run.Partitioned();
        AgentAccount account = new(@"CORP\installer", RunAsPassword);
        run.Server.OnRunStepAccounts(_ => new AgentStepAccounts(account, []));
        IAccountSession? ranAs = null;
        run.ToolRunner.AnswerExitCode = (_, _, options) =>
        {
            ranAs = options.Account;

            return 0;
        };

        StepResult result = await run.Steps.RunAsync(step, run.Context(SequencePhase.Windows), TestContext.Current.CancellationToken);

        Assert.Equal(StepOutcome.Done, result.Outcome);
        Assert.Equal(@"CORP\installer", ranAs?.UserName);
        Assert.Equal([@"sign in CORP\installer", @"admit CORP\installer", @"sign out CORP\installer"], run.Accounts.Events);
    }

    [Fact]
    public async Task RunsAsTheAgentWhenNoAccountIsGiven()
    {
        RunScriptStep step = Script(SequencePhase.WindowsPE);
        using StepRunnerFixture run = new([step]);
        bool hadAccount = true;
        run.ToolRunner.AnswerExitCode = (_, _, options) =>
        {
            hadAccount = options.Account is not null;

            return 0;
        };

        StepResult result = await run.Steps.RunAsync(step, run.Context(), TestContext.Current.CancellationToken);

        Assert.Equal(StepOutcome.Done, result.Outcome);
        Assert.False(hadAccount);

        // A step with no shares and no run-as never reports early or asks for accounts.
        Assert.DoesNotContain(run.Server.Calls, call => call.StartsWith("run-accounts", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RunAsIsRefusedInWindowsPEBeforeAnyAccountIsFetched()
    {
        RunScriptStep step = Script(SequencePhase.WindowsPE, runAs: new AccountReference(Guid.NewGuid(), null));
        using StepRunnerFixture run = new([step]);

        StepResult result = await run.Steps.RunAsync(step, run.Context(), TestContext.Current.CancellationToken);

        Assert.Equal(StepResult.Failed(StepAccounts.RunAsInWindowsPE), result);
        Assert.Empty(run.Server.Calls);
        Assert.Empty(run.ToolRunner.Calls);
    }

    [Fact]
    public async Task NoLineOrErrorHoldsAPassword()
    {
        AccountReference reference = new(Guid.NewGuid(), null);
        RunScriptStep step = Script(SequencePhase.Windows, [Share], reference);
        using StepRunnerFixture run = new([step]);
        run.Partitioned();
        AgentAccount account = new(@"CORP\installer", RunAsPassword);
        AgentStepAccounts accounts = new(account, [s_drivers]);
        run.Server.OnRunStepAccounts(_ => accounts);
        run.ToolRunner.AnswerExitCode = (_, _, _) => 0;

        await run.Steps.RunAsync(step, run.Context(SequencePhase.Windows), TestContext.Current.CancellationToken);

        foreach (string message in (await run.SentLinesAsync()).Select(line => line.Message).Concat(run.Accounts.Events))
        {
            Assert.DoesNotContain(SharePassword, message, StringComparison.Ordinal);
            Assert.DoesNotContain(RunAsPassword, message, StringComparison.Ordinal);
        }

        Assert.DoesNotContain(RunAsPassword, account.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(SharePassword, s_drivers.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ADryRunSignsInNobodyConnectsNothingAndRunsNothing()
    {
        AccountReference reference = new(Guid.NewGuid(), null);
        RunScriptStep step = Script(SequencePhase.Windows, [Share], reference);
        using StepRunnerFixture run = new([step]);
        run.Partitioned();
        StringWriter console = new();
        AgentLog log = new(run.Time, console);
        run.Server.OnRunStepAccounts(_ => new AgentStepAccounts(new AgentAccount(@"CORP\installer", RunAsPassword), [s_drivers]));

        StepAccounts dryRun = new(run.Server, run.Session, _ => Task.CompletedTask, AccountTools.DryRun(log), log, run.Time);
        RunScriptStepRunner runner = new(new DryRunToolRunner(log), run.Downloads, run.Session, log, run.WorkDirectory);

        StepResult result = await dryRun.RunAsync(
            step,
            run.Context(SequencePhase.Windows),
            account => runner.RunAsync(step, run.Context(SequencePhase.Windows), account, TestContext.Current.CancellationToken),
            TestContext.Current.CancellationToken);

        Assert.Equal(StepOutcome.Done, result.Outcome);
        string written = console.ToString();
        Assert.Contains(@"Dry run: CORP\installer is not signed in.", written, StringComparison.Ordinal);
        Assert.Contains($@"Dry run: not connected: {s_drivers.Path} as CORP\svc-drivers, for CORP\installer.", written, StringComparison.Ordinal);
        Assert.Contains(@"Dry run: not run as CORP\installer", written, StringComparison.Ordinal);
        Assert.DoesNotContain(RunAsPassword, written, StringComparison.Ordinal);
        Assert.DoesNotContain(SharePassword, written, StringComparison.Ordinal);
    }
}
