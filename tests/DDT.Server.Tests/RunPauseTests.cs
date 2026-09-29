// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Net;
using System.Net.Http.Json;
using System.Threading.Channels;
using DDT.Contracts.Agents;
using DDT.Contracts.Deployments;
using DDT.Contracts.Machines;
using DDT.Contracts.Sequences;
using DDT.Core.Sequences;
using DDT.Server.Authentication;
using DDT.Server.Deployments;
using DDT.Server.Live;
using DDT.Server.Machines;
using Microsoft.EntityFrameworkCore;
using Xunit;
using static DDT.Server.Tests.TestReports;
using static DDT.Server.Tests.TreeSequences;

namespace DDT.Server.Tests;

// A Pause step waits until someone continues the run at the machine or on its page.
// The variables that steps set are reported, kept and pushed to whoever watches the machine.
public sealed class RunPauseTests(DdtApplication application) : IClassFixture<DdtApplication>
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private sealed record Paused(DeployingMachine Machine, AgentRun Run, RunScriptStep Before, PauseStep Pause, RunScriptStep After);

    private async Task<Paused> AssignedAsync()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        DeployingMachine machine = await DeployingMachine.ApprovedAsync(application, administrator);
        RunScriptStep before = Script("Before");
        PauseStep pause = Pause() with { ContinueAfterMinutes = 30 };
        RunScriptStep after = Script("After");
        SequenceDefinition definition = SequenceRequests.Definition(before, pause, after) with
        {
            Variables = [new VariableDeclaration { Name = "Office", Default = "Standard", SetBySteps = true }],
            Inputs =
            [
                new InputDeclaration
                {
                    Name = "Installer",
                    Label = "Installer",
                    Kind = InputKind.Account,
                    Account = new AccountDestination { Hosts = ["files.example"] },
                },
            ],
        };
        Assert.Empty(SequenceValidator.Validate(definition.Normalised()));
        SequenceView sequence = await administrator.CreatedSequenceAsync(definition);
        Guid runId = (await administrator.AssignedAsync(machine.Id, sequence.Id)).Id;
        AgentRun run = (await machine.NextAsync()).Run!;

        Assert.Equal(runId, run.Id);
        await machine.ReportOkAsync(run.Id, Report(DeploymentState.Running, []) with { Activity = RunActivity.Preparing });

        return new Paused(machine, run, before, pause, after);
    }

    private static AgentRunReport PausedAt(Paused paused, int pass = 1, string message = "Check the BIOS of 0000-0000.") =>
        Report(DeploymentState.Running, [Visit(paused.Before, StepState.Done), Visit(paused.Pause, StepState.Running, pass)]) with
        {
            Activity = RunActivity.Paused,
            PauseMessage = message,
        };

    private static async Task<AgentRunReportResult> ReportedAsync(DeployingMachine machine, Guid runId, AgentRunReport report)
    {
        await machine.ReportOkAsync(runId, report);

        return machine.LastReported!;
    }

    private static Task<HttpResponseMessage> ContinueAsync(SignedInClient client, Guid machineId, Guid stepId, int pass) =>
        client.PostAsync($"/api/machines/{machineId}/deployments/current/continue", new ContinueRunRequest(stepId, pass));

    [Fact]
    public async Task ThePageAndTheListShowThePauseTheRunWaitsAt()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        Paused paused = await AssignedAsync();
        using DeployingMachine machine = paused.Machine;

        AgentRunReportResult waiting = await ReportedAsync(machine, paused.Run.Id, PausedAt(paused));

        Assert.Equal(5, waiting.ReportAfterSeconds);
        Assert.Null(waiting.ContinueStepId);

        DeploymentView view = await administrator.RunAsync(paused.Run.Id);
        Assert.True(view.Summary.Waiting);
        Assert.Equal("Check the BIOS of 0000-0000.", view.Summary.PauseMessage);
        RunPauseView pause = view.Pause!;
        Assert.Equal((paused.Pause.Id, 1, "Check the BIOS of 0000-0000."), (pause.StepId, pause.Pass, pause.Message));
        Assert.Equal(pause.SinceUtc!.Value.AddMinutes(30), pause.ContinuesUtc);
        MachineSummary listed = Assert.Single(await RegisteredMachine.ReadAsync<IReadOnlyList<MachineSummary>>(await administrator.GetAsync("/api/machines")), m => m.Id == machine.Id);
        Assert.Equal((true, "Check the BIOS of 0000-0000."), (listed.Deployment!.Waiting, listed.Deployment.PauseMessage));

        using (SignedInClient viewer = await application.SignInAsync(DdtRoleNames.Viewer))
        {
            Assert.Equal(HttpStatusCode.Forbidden, (await ContinueAsync(viewer, machine.Id, paused.Pause.Id, 1)).StatusCode);
        }
    }

    [Fact]
    public async Task AnOperatorContinuesThePauseThePageShowedAndTheAgentLearnsItUntilThePauseIsOver()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        Paused paused = await AssignedAsync();
        using DeployingMachine machine = paused.Machine;
        await ReportedAsync(machine, paused.Run.Id, PausedAt(paused));

        // A click meant for another visit or another step continues nothing. The answer has the run as it is.
        HttpResponseMessage later = await ContinueAsync(administrator, machine.Id, paused.Pause.Id, 2);
        Assert.Equal(HttpStatusCode.Conflict, later.StatusCode);
        Assert.Equal(1, (await later.Content.ReadFromJsonAsync<DeploymentView>(TestJson.Options, Cancellation))!.Pause!.Pass);
        Assert.Equal(HttpStatusCode.Conflict, (await ContinueAsync(administrator, machine.Id, paused.Before.Id, 1)).StatusCode);

        DeploymentView continued = await RegisteredMachine.ReadAsync<DeploymentView>(await ContinueAsync(administrator, machine.Id, paused.Pause.Id, 1));

        Assert.Null(continued.Pause);
        Assert.False(continued.Summary.Waiting);
        Assert.Null(continued.Summary.PauseMessage);
        Assert.Equal(HttpStatusCode.Conflict, (await ContinueAsync(administrator, machine.Id, paused.Pause.Id, 1)).StatusCode);

        string subject = paused.Run.Id.ToString("D");
        string audit = await application.QueryAsync(database => database.AuditEvents
            .Where(e => e.SubjectId == subject && e.Action == AuditActions.DeploymentContinued)
            .Select(e => e.Detail!)
            .SingleAsync(Cancellation));
        Assert.Equal($"{paused.Run.SequenceName} on machine {machine.Id:D}, at Check the BIOS ({paused.Pause.Id:D}), visit 1.", audit);

        // Every answer carries it until the pause is over, and none after.
        AgentRunReportResult told = await ReportedAsync(machine, paused.Run.Id, PausedAt(paused));
        Assert.Equal(((Guid?)paused.Pause.Id, (int?)1), (told.ContinueStepId, told.ContinuePass));
        Assert.Equal(paused.Pause.Id, (await ReportedAsync(machine, paused.Run.Id, PausedAt(paused))).ContinueStepId);

        AgentRunReportResult goingOn = await ReportedAsync(
            machine,
            paused.Run.Id,
            Running(Visit(paused.Before, StepState.Done), Visit(paused.Pause, StepState.Done), Visit(paused.After, StepState.Running)));

        Assert.Null(goingOn.ContinueStepId);
        Assert.Null(goingOn.ContinuePass);
        Assert.Null(goingOn.ReportAfterSeconds);

        Deployment stored = await application.QueryAsync(database => database.Deployments.AsNoTracking().SingleAsync(d => d.Id == paused.Run.Id, Cancellation));
        Assert.Equal(((Guid?)null, (int?)null, (string?)null, (Guid?)null), (stored.PauseStepId, stored.PausePass, stored.PauseMessage, stored.ContinueStepId));
    }

    // A report only counts as paused when its current step is a running Pause step.
    // The message is cut to the length of its column.
    [Fact]
    public async Task APauseIsTheCurrentPauseStepThatRuns()
    {
        Paused paused = await AssignedAsync();
        using DeployingMachine machine = paused.Machine;

        await machine.ReportOkAsync(
            paused.Run.Id,
            Report(DeploymentState.Running, [Visit(paused.Before, StepState.Running)]) with { Activity = RunActivity.Paused, PauseMessage = "Not a pause." });

        Deployment notPaused = await application.QueryAsync(database => database.Deployments.AsNoTracking().SingleAsync(d => d.Id == paused.Run.Id, Cancellation));
        Assert.Null(notPaused.PauseStepId);
        Assert.False(DeploymentSummaries.Waiting(notPaused));

        await machine.ReportOkAsync(paused.Run.Id, PausedAt(paused, message: "A NUL\0 " + new string('x', 2000)));

        Deployment waiting = await application.QueryAsync(database => database.Deployments.AsNoTracking().SingleAsync(d => d.Id == paused.Run.Id, Cancellation));
        Assert.Equal(DeploymentLimits.MaxPauseMessageLength, waiting.PauseMessage!.Length);
        Assert.StartsWith("A NUL x", waiting.PauseMessage, StringComparison.Ordinal);

        // A run stopped at its pause isn't waiting for anyone anymore.
        MachineSummary stopped = await RegisteredMachine.ReadAsync<MachineSummary>(await (await application.AdministratorAsync()).EndCurrentAsync(machine.Id));
        Assert.Equal((DeploymentState.Failed, false, (string?)null), (stopped.Deployment!.State, stopped.Deployment.Waiting, stopped.Deployment.PauseMessage));
    }

    [Fact]
    public async Task TheVariablesStepsSetAreKeptBoundedAndPushedToWhoeverWatchesTheMachine()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        Paused paused = await AssignedAsync();
        using DeployingMachine machine = paused.Machine;
        await using LiveListener listener = await LiveListener.StartAsync(application, administrator);
        ChannelReader<RunVariablesChangedEvent> changes = listener.Listen<RunVariablesChangedEvent>(LiveEvents.RunVariablesChanged);
        await listener.WatchAsync(machine.Id);
        string longValue = "A NUL\0" + new string('y', 2000);

        await machine.ReportOkAsync(
            paused.Run.Id,
            Running(Visit(paused.Before, StepState.Done)) with
            {
                Variables = new Dictionary<string, string>
                {
                    ["Office"] = "ProPlus",
                    [MachineVariableNames.LastExitCode] = "0",
                    ["Installer"] = "not a value",
                    ["not a name"] = "x",
                    ["Long"] = longValue,
                },
            });

        RunVariablesChangedEvent pushed = await LiveListener.NextAsync(changes);
        Assert.Equal((machine.Id, paused.Run.Id), (pushed.MachineId, pushed.DeploymentId));
        Assert.Equal(["LastExitCode", "Long", "Office"], pushed.Variables.Keys.Order(StringComparer.Ordinal));
        Assert.Equal("ProPlus", pushed.Variables["Office"]);
        Assert.Equal(RunVariables.MaxValueLength, pushed.Variables["Long"].Length);
        Assert.StartsWith("A NULy", pushed.Variables["Long"], StringComparison.Ordinal);

        // A report without them leaves them as they were. A report with a change merges it in.
        await machine.ReportOkAsync(paused.Run.Id, Running(Visit(paused.Before, StepState.Done), Visit(paused.Pause, StepState.Running)));
        await machine.ReportOkAsync(
            paused.Run.Id,
            Running(Visit(paused.Before, StepState.Done), Visit(paused.Pause, StepState.Running)) with { Variables = new Dictionary<string, string> { ["Office"] = "Home" } });

        pushed = await LiveListener.NextAsync(changes);
        Assert.Equal(("Home", "0"), (pushed.Variables["Office"], pushed.Variables[MachineVariableNames.LastExitCode]));

        DeploymentView view = await administrator.RunAsync(paused.Run.Id);
        Assert.Equal(pushed.Variables.OrderBy(v => v.Key), view.Variables!.OrderBy(v => v.Key));

        // A report with more variables than the agent keeps is refused, not trimmed.
        HttpResponseMessage many = await machine.ReportAsync(
            paused.Run.Id,
            Running(Visit(paused.Before, StepState.Done)) with
            {
                Variables = Enumerable.Range(0, RunVariables.MaxCount + 1).ToDictionary(index => $"V{index}", index => "x"),
            });
        Assert.Equal(HttpStatusCode.BadRequest, many.StatusCode);
    }
}
