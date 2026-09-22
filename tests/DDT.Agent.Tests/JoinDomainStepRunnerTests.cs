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

public sealed class JoinDomainStepRunnerTests : IDisposable
{
    private const string Domain = "corp.example.test";

    private readonly StepRunnerFixture _run = new([TestRuns.Join]);

    public void Dispose() => _run.Dispose();

    private static string Password => TestRuns.JoinAccount.Password;

    private StepContext InWindows => _run.Context(SequencePhase.Windows);

    [Fact]
    public async Task ReportsTheStepAsRunningBeforeItAsksForTheAccountThenJoinsAndAsksForARestart()
    {
        _run.Server.OnRunCredentials(_ => TestRuns.JoinAccount);

        StepResult result = await _run.JoinDomain.RunAsync(TestRuns.Join, InWindows, TestContext.Current.CancellationToken);

        Assert.Equal(StepResult.RebootRequired(), result);
        Assert.Equal(["run-report Running session", $"run-credentials {TestRuns.Join.Id} session"], _run.Server.Calls);
        Assert.Equal([$"join {Domain}"], _run.Tools.Calls);
        Assert.Same(TestRuns.JoinAccount, _run.Tools.JoinedWith);
        Assert.Equal([100], _run.Progress.Values);
    }

    [Fact]
    public async Task TriesAgainWhileNoDomainControllerAnswers()
    {
        AdvancingTimeProvider time = new();
        _run.Server.OnRunCredentials(_ => TestRuns.JoinAccount);
        _run.Tools.JoinAnswers(DomainJoinErrors.NoSuchDomain, DomainJoinErrors.RpcServerUnavailable);

        StepResult result = await Runner(_run.Tools, _run.Log, time).RunAsync(TestRuns.Join, InWindows, TestContext.Current.CancellationToken);

        Assert.Equal(StepResult.RebootRequired(), result);
        Assert.Equal(3, _run.Tools.Calls.Count);
        Assert.Equal(TimeSpan.FromSeconds(6), time.Elapsed);
        Assert.Contains(
            await _run.SentLinesAsync(),
            line => line.Level == AgentLogLevel.Warning && line.Message.EndsWith("(error 1355). Check that the DNS server this machine gets from DHCP knows the domain. Trying again in 2 s.", StringComparison.Ordinal));
    }

    [Fact]
    public async Task GivesUpOnceNoDomainControllerHasAnsweredForFiveMinutes()
    {
        AdvancingTimeProvider time = new();
        _run.Server.OnRunCredentials(_ => TestRuns.JoinAccount);
        _run.Tools.JoinAnswers([.. Enumerable.Repeat(DomainJoinErrors.NoSuchDomain, 100)]);

        StepResult result = await Runner(_run.Tools, _run.Log, time).RunAsync(TestRuns.Join, InWindows, TestContext.Current.CancellationToken);

        Assert.Equal(StepResult.Failed(DomainJoinErrors.Describe(DomainJoinErrors.NoSuchDomain, Domain)), result);

        // At 0, 2, 6, 14 and 30 seconds, then every 30 seconds until the next would come after five minutes.
        Assert.Equal(14, _run.Tools.Calls.Count);
        Assert.Equal(JoinDomainStepRunner.RetryFor, time.Elapsed);
    }

    [Fact]
    public async Task AnAnswerThatWillNotChangeFailsTheStepAtOnce()
    {
        _run.Server.OnRunCredentials(_ => TestRuns.JoinAccount);
        _run.Tools.JoinAnswers(DomainJoinErrors.AccountReuseBlocked);

        StepResult result = await _run.JoinDomain.RunAsync(TestRuns.Join, InWindows, TestContext.Current.CancellationToken);

        Assert.Equal(StepOutcome.Failed, result.Outcome);
        Assert.Contains("KB5020276", result.Error, StringComparison.Ordinal);
        Assert.Single(_run.Tools.Calls);
        Assert.Empty(_run.Time.Delays);
    }

    [Theory]
    [InlineData(DomainJoinErrors.AccountReuseBlocked, "reusing computer accounts (KB5020276)")]
    [InlineData(DomainJoinErrors.LogonFailure, "did not accept the join account's user name or password")]
    [InlineData(DomainJoinErrors.AccessDenied, "may not add this computer to corp.example.test")]
    [InlineData(DomainJoinErrors.MachineAccountQuotaExceeded, "ms-DS-MachineAccountQuota")]
    [InlineData(DomainJoinErrors.TimeSkew, "clock is too far from the domain controller's")]
    [InlineData(DomainJoinErrors.NoSuchDomain, "The domain corp.example.test was not found")]
    [InlineData(DomainJoinErrors.RpcServerUnavailable, "No domain controller of corp.example.test could be reached")]
    [InlineData(DomainJoinErrors.AlreadyJoined, "already joined to a domain")]
    [InlineData(DomainJoinErrors.AccountExists, "cannot take over")]
    [InlineData(4242, "Joining corp.example.test failed with error 4242: ")]
    public void SaysWhatAnAnswerMeansAndWhatToChange(int code, string says)
    {
        string message = DomainJoinErrors.Describe(code, Domain);

        Assert.Contains(says, message, StringComparison.Ordinal);
        Assert.Contains($"error {code}", message, StringComparison.Ordinal);
    }

    [Fact]
    public void OnlyAnswersAboutReachingTheDomainAreTriedAgain()
    {
        int[] transient = [.. new[] { 5, 53, 1231, 1311, 1326, 1355, 1398, 1722, 2224, 2691, 2732, 8557 }.Where(DomainJoinErrors.IsTransient)];

        Assert.Equal([53, 1231, 1311, 1355, 1722], transient);
    }

    [Fact]
    public async Task NeverLogsTheAccount()
    {
        _run.Server.OnRunCredentials(_ => TestRuns.JoinAccount).OnRunCredentials(_ => TestRuns.JoinAccount);
        _run.Tools.JoinAnswers(DomainJoinErrors.NoSuchDomain, DomainJoinErrors.LogonFailure);

        StepResult failed = await _run.Steps.RunAsync(TestRuns.Join, InWindows, TestContext.Current.CancellationToken);
        StepResult joined = await _run.Steps.RunAsync(TestRuns.Join, InWindows, TestContext.Current.CancellationToken);

        Assert.Equal(StepOutcome.Failed, failed.Outcome);
        Assert.Equal(StepResult.RebootRequired(), joined);
        List<AgentLogLine> lines = await _run.SentLinesAsync();
        Assert.Contains(lines, line => line.Message == "Joining corp.example.test, with the computer account in OU=Workstations,DC=corp,DC=example,DC=test.");
        Assert.All(
            [.. lines.Select(line => line.Message), failed.Error!],
            text => Assert.False(
                text.Contains(Password, StringComparison.Ordinal) || text.Contains(TestRuns.JoinAccount.UserName, StringComparison.OrdinalIgnoreCase),
                text));
        Assert.DoesNotContain(Password, TestRuns.JoinAccount.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ADryRunFetchesTheAccountButJoinsNothingAndNeverLogsIt()
    {
        StringWriter console = new();
        AgentLog log = new(_run.Time, console);
        _run.Server.OnRunCredentials(_ => TestRuns.JoinAccount);

        StepResult result = await Runner(new DryRunDomainJoiner(log), log, _run.Time).RunAsync(TestRuns.Join, InWindows, TestContext.Current.CancellationToken);

        Assert.Equal(StepResult.RebootRequired(), result);
        Assert.Contains($"run-credentials {TestRuns.Join.Id} session", _run.Server.Calls);
        string written = console.ToString();
        Assert.Contains("Dry run: this computer does not join corp.example.test.", written, StringComparison.Ordinal);
        Assert.DoesNotContain(Password, written, StringComparison.Ordinal);
        Assert.DoesNotContain(TestRuns.JoinAccount.UserName, written, StringComparison.OrdinalIgnoreCase);
    }

    private JoinDomainStepRunner Runner(IDomainJoiner joiner, AgentLog log, TimeProvider time) =>
        new(joiner, _run.Server, _run.Session, _ => Task.CompletedTask, log, time);
}
