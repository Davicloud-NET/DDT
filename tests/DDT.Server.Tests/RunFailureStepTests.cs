// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Security.Cryptography;
using DDT.Contracts.Agents;
using DDT.Contracts.Deployments;
using DDT.Contracts.Sequences;
using DDT.Server.Images;
using DDT.Server.Machines;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Xunit;
using static DDT.Server.Tests.TestReports;

namespace DDT.Server.Tests;

// A failed run names the step it failed at, also when that step began and failed between two reports.
public sealed class RunFailureStepTests : IDisposable
{
    private readonly LoggedApplication _application = new();

    private const string ExitCode = "The script ended with exit code 1, which is not one of its success codes (0).";

    private static RunScriptStep Script(string name, bool mayFail = false) => new()
    {
        Id = Guid.NewGuid(),
        Name = name,
        Phase = SequencePhase.Windows,
        Interpreter = ScriptInterpreter.Cmd,
        Script = "exit 1",
        ContinueOnError = mayFail,
    };

    public void Dispose() => _application.Dispose();

    private async Task<(DeployingMachine Machine, AgentRun Run)> AssignedAsync(params SequenceStep[] after)
    {
        SignedInClient administrator = await _application.AdministratorAsync();
        DeployingMachine machine = await DeployingMachine.ApprovedAsync(_application, administrator);
        Image image = await _application.SeedImageAsync(RandomNumberGenerator.GetBytes(4096));
        SequenceView sequence = await administrator.CreatedSequenceAsync(SequenceRequests.Definition([.. SequenceRequests.Minimal(image.Id).Steps, .. after]));

        await administrator.AssignedAsync(machine.Id, sequence.Id);

        return (machine, (await machine.NextAsync()).Run!);
    }

    private async Task<DeploymentSummary> SummaryAsync(Guid runId) => (await (await _application.AdministratorAsync()).RunAsync(runId)).Summary;

    private Task<List<string?>> AuditAsync(Guid runId)
    {
        string subject = runId.ToString("D");

        return _application.QueryAsync(database => database.AuditEvents
            .Where(e => e.SubjectId == subject && e.Action == AuditActions.DeploymentFailed)
            .Select(e => e.Detail)
            .ToListAsync(TestContext.Current.CancellationToken));
    }

    private string Logged(Guid runId) =>
        Assert.Single(_application.Log.Entries, entry => entry.Level == LogLevel.Warning && entry.Message.StartsWith($"Deployment {runId} ", StringComparison.Ordinal)).Message;

    // Quick steps share one report: the step before is done and the next has failed before the agent reports again.
    [Fact]
    public async Task AFailureNamesTheStepThatBeganAndFailedSinceTheLastReport()
    {
        (DeployingMachine machine, AgentRun run) = await AssignedAsync();
        using DeployingMachine _ = machine;
        (SequenceStep partition, SequenceStep apply) = (run.Sequence.Steps[0], run.Sequence.Steps[1]);

        await machine.ReportOkAsync(run.Id, Running() with { Activity = RunActivity.Preparing });
        await machine.ReportOkAsync(
            run.Id,
            Report(DeploymentState.Failed, [Step(partition, StepState.Done), Step(apply, StepState.Failed, ExitCode)]) with { Error = ExitCode });

        DeploymentSummary failed = await SummaryAsync(run.Id);

        Assert.Equal((DeploymentState.Failed, 1, "Apply"), (failed.State, failed.StepIndex, failed.StepName));
        Assert.Equal([$"{failed.Title} on machine {machine.Id:D} at Apply: {ExitCode}"], await AuditAsync(run.Id));
        Assert.Equal($"Deployment {run.Id} of {failed.Title} failed on machine {machine.Id} at step Apply: {ExitCode}", Logged(run.Id));
    }

    // The step the agent last named as running stays the one the run failed at.
    [Fact]
    public async Task AFailureKeepsTheStepThatWasRunning()
    {
        (DeployingMachine machine, AgentRun run) = await AssignedAsync();
        using DeployingMachine _ = machine;
        (SequenceStep partition, SequenceStep apply) = (run.Sequence.Steps[0], run.Sequence.Steps[1]);

        await machine.ReportOkAsync(run.Id, Running(Step(partition, StepState.Running)));
        await machine.ReportOkAsync(
            run.Id,
            Report(DeploymentState.Failed, [Step(partition, StepState.Failed, "The disk is gone.")]) with { Error = "The disk is gone." });

        DeploymentSummary failed = await SummaryAsync(run.Id);

        Assert.Equal((0, "Partition"), (failed.StepIndex, failed.StepName));
        Assert.Equal(StepState.Pending, (await (await _application.AdministratorAsync()).RunAsync(run.Id)).Steps.Single(step => step.StepId == apply.Id).State);
    }

    // A step that may fail has its own error. The run then ended for another reason, at none of its steps.
    [Fact]
    public async Task AFailureThatNoStepCausedNamesNoStepThatWasAllowedToFail()
    {
        RunScriptStep allowed = Script("Tidy up", mayFail: true);
        (DeployingMachine machine, AgentRun run) = await AssignedAsync(allowed, Script("Last"));
        using DeployingMachine _ = machine;
        StepRunState[] steps =
        [
            Step(run.Sequence.Steps[0], StepState.Done),
            Step(run.Sequence.Steps[1], StepState.Done),
            Step(allowed, StepState.Failed, ExitCode),
        ];

        await machine.ReportOkAsync(run.Id, Running() with { Activity = RunActivity.Preparing });
        await machine.ReportOkAsync(
            run.Id,
            Report(DeploymentState.Failed, steps) with { Phase = SequencePhase.Windows, Error = "The run's state could not be saved: the disk is full." });

        DeploymentSummary failed = await SummaryAsync(run.Id);

        Assert.Equal((DeploymentState.Failed, null, null), (failed.State, failed.StepIndex, failed.StepName));
        Assert.Equal([$"{failed.Title} on machine {machine.Id:D}: The run's state could not be saved: the disk is full."], await AuditAsync(run.Id));
        Assert.Equal(
            $"Deployment {run.Id} of {failed.Title} failed on machine {machine.Id}, at none of its steps: The run's state could not be saved: the disk is full.",
            Logged(run.Id));
    }
}
