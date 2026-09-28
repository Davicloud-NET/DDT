// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using DDT.Contracts;
using DDT.Contracts.Deployments;
using DDT.Contracts.Images;
using DDT.Contracts.Machines;
using DDT.Contracts.Rules;
using DDT.Contracts.Sequences;
using DDT.Core.CloudInit;
using DDT.Core.Disks;
using DDT.Server.Data;
using Xunit;

namespace DDT.E2E;

// Whole task sequences, run by the real host and the published agent in dry runs, which change nothing on this computer.
// Every run goes through Windows PE and, after the hand-over, the installed Windows in one agent process.
[Trait("Category", "E2E")]
[Collection(DryRunCollection.Name)]
public sealed class DryRunTests(DryRunLab lab)
{
    // The agent's exit codes: stopped, and a run that finished.
    private const int Stopped = 0;
    private const int Deployed = 4;

    private const string StoppedError = "Stopped by admin.";
    private const string InterruptedError = "The machine restarted or the agent stopped while this step ran.";
    private const string TokenRefused = "The server no longer accepts this machine's token";

    private static readonly TimeSpan s_runTimeout = TimeSpan.FromMinutes(3);

    [Fact(Timeout = 600_000)]
    public async Task AWholeSequenceRunsThroughBothPhasesAndTheAgentRemovesItself()
    {
        lab.SkipWhenUnavailable();
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;

        WholeRunSequence steps = new(lab);
        SequenceView sequence = await lab.CreateSequenceAsync("Whole run", steps.Steps, cancellationToken);

        await using AgentProcess agent = lab.StartAgent();
        MachineSummary machine = await lab.WaitForMachineAsync(agent, cancellationToken);
        Assert.Equal(MachineState.Pending, machine.State);
        await lab.Live.WatchAsync(machine.Id, cancellationToken);

        // Once approved, the agent sends the lines it kept while it waited, which belong to no run.
        Assert.Equal(MachineState.Approved, (await lab.ApproveAsync(machine.Id, null, cancellationToken)).State);
        MachineLogAppendedEvent firstPush = await lab.Live.WaitForLogPushAsync(machine.Id, TimeSpan.FromMinutes(1), () => agent.Output.Tail(), cancellationToken);

        AssignSequenceRequest assignment = new(sequence.Id, ComputerName(agent));
        DeploymentSummary assigned = (await lab.AssignAsync(machine.Id, assignment, cancellationToken)).Deployment!;
        Assert.Equal((DeploymentState.Assigned, DeploymentSource.Web), (assigned.State, assigned.Source));

        Assert.Equal(Deployed, await agent.WaitForExitAsync(s_runTimeout, cancellationToken));
        AssertRemovedItself(agent);
        Assert.Equal(1, agent.Output.Count("Windows restarts once more, which deletes what is left of the agent."));

        DeploymentView run = await lab.RunAsync(assigned.Id, cancellationToken);
        Assert.Equal((DeploymentState.Done, null), (run.Summary.State, run.Summary.Error));
        AssertEveryStepEndedAsExpected(run, steps, machine.Id);
        await AssertTheRunKeepsWhatItWasAssignedAsync(sequence, run, steps, assigned.Id, cancellationToken);

        // Partitioned once: after its restart, Windows PE found the run's partitions again.
        Assert.Equal(1, agent.Output.Count($"Dry run: the new volumes are the directories under {agent.Root}."));
        Assert.Equal(1, agent.Output.Count($"Dry run: diskpart is not run. The run's partitions are the directories under {agent.Root} again."));
        Assert.Equal(1, agent.Output.Count("Dry run: wimlib is not run. It would apply image 1 (DDT E2E Windows"));
        Assert.Equal(1, agent.Output.Count($"Dry run: this computer does not join {DryRunLab.Domain}."));

        // Every start after the first went on with the run: Windows PE after its restart, and Windows after the
        // hand-over and after each of its two restarts. The answer file and the join account were read once each.
        IReadOnlyList<AuditEvent> audit = await lab.AuditAsync(assigned.Id, cancellationToken);
        Assert.Equal(
            ["WindowsPE", "Windows", "Windows", "Windows"],
            audit.Where(entry => entry.Action == "deployment.resumed").OrderBy(entry => entry.Id).Select(ResumedIn));
        Assert.Equal(2, audit.Count(entry => entry.Action == "deployment.secret-read"));
        Assert.All(audit.Where(entry => entry.Action is "deployment.resumed" or "deployment.secret-read"), entry => Assert.NotNull(entry.SourceAddress));

        MachineLogEntry[] lines = await CheckLogAsync(machine.Id, assigned.Id, steps.Apply.Id, firstPush, cancellationToken);
        AssertTheSkipIsLoggedOnce(lines, steps.Skipped);

        lab.AssertClean(
            [agent],
            ["Step Fails, and the run goes on failed after"],
            ("The run's detail", JsonSerializer.Serialize(run, DdtJsonContext.Default.DeploymentView)),
            ("The machine's log", string.Join(Environment.NewLine, lines.Select(line => line.Message))));
    }

    [Fact(Timeout = 600_000)]
    public async Task AStopInTheWindowsPhaseEndsTheRunAndTheAgentRemovesItself()
    {
        lab.SkipWhenUnavailable();
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;

        RunScriptStep packaged = Script("Script from a large package", SequencePhase.Windows, "Get-ChildItem", ScriptInterpreter.PowerShell) with
        {
            PackageId = lab.LargeFiles.Id,
        };
        RunScriptStep after = Script("After the stop", SequencePhase.Windows, "echo after");
        SequenceView sequence = await lab.CreateSequenceAsync(
            "Stopped in Windows",
            [Partition(), new ApplyImageStep { Id = Guid.CreateVersion7(), Name = "Apply the image", ImageId = lab.Image.Id }, packaged, after],
            cancellationToken);

        await using AgentProcess agent = lab.StartAgent(slowDownloads: true);
        (Guid machineId, Guid runId) = await AuthorizeAsync(agent, sequence, null, cancellationToken);

        // While the service in Windows downloads the package, which takes far longer than the agent's next call.
        await agent.WaitForDownloadAsync(lab.LargeFiles.Sha256, s_runTimeout, cancellationToken);
        DeploymentSummary stopped = (await lab.Api.SendAsync(
            HttpMethod.Delete,
            $"api/machines/{machineId:D}/deployments/current",
            DdtJsonContext.Default.MachineSummary,
            cancellationToken)).Deployment!;
        Assert.Equal((DeploymentState.Failed, StoppedError), (stopped.State, stopped.Error));

        // The agent learns of the stop at its next call, which stops the step, and runs nothing after it.
        Assert.Equal(Stopped, await agent.WaitForExitAsync(s_runTimeout, cancellationToken));
        Assert.Equal(1, agent.Output.Count($"Step {packaged.Name} was stopped"));
        Assert.Equal(0, agent.Output.Count($"Step {after.Name} begins."));
        AssertRemovedItself(agent);
        Assert.Equal(0, agent.Output.Count("Windows restarts once more"));

        // The server keeps the run as the stop left it: the package's step failed with it if a call had reported it as
        // running, and is pending otherwise.
        DeploymentView run = await lab.RunAsync(runId, cancellationToken);
        Assert.Equal((DeploymentState.Failed, SequencePhase.Windows, StoppedError), (run.Summary.State, run.Summary.Phase, run.Summary.Error));
        Assert.Equal([StepState.Done, StepState.Done], run.Steps.Take(2).Select(step => step.State));
        Assert.Contains((run.Steps[2].State, run.Steps[2].Error), (IEnumerable<(StepState, string?)>)[(StepState.Pending, null), (StepState.Failed, StoppedError)]);
        Assert.Equal((StepState.Pending, null), (run.Steps[3].State, run.Steps[3].Error));

        lab.AssertClean(
            [agent],
            [TokenRefused, $"Step {packaged.Name} was stopped."],
            ("The run's detail", JsonSerializer.Serialize(run, DdtJsonContext.Default.DeploymentView)));
    }

    [Fact(Timeout = 600_000)]
    public async Task AnAgentKilledDuringApplyImageFailsTheInterruptedStepWhenItStartsAgain()
    {
        lab.SkipWhenUnavailable();
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;

        ApplyImageStep apply = new() { Id = Guid.CreateVersion7(), Name = "Apply the large image", ImageId = lab.LargeImage.Id };
        SequenceView sequence = await lab.CreateSequenceAsync(
            "Killed while applying",
            [Partition(), apply, Script("After the image", SequencePhase.WindowsPE, "echo after")],
            cancellationToken);

        await using AgentProcess first = lab.StartAgent(slowDownloads: true);
        (_, Guid runId) = await AuthorizeAsync(first, sequence, null, cancellationToken);

        // While the image downloads, as a power loss would.
        await first.WaitForDownloadAsync(lab.LargeImage.Sha256, s_runTimeout, cancellationToken);
        await first.KillAsync();
        Assert.True(Directory.Exists(first.Root), "The dry run's disk went with the agent.");

        // Until the run ends either way, so a wrong end fails at once rather than after the timeout.
        await using AgentProcess second = lab.StartAgent(first.DryRunId);
        DeploymentView run = await Eventually.GetAsync(
            new Expectation("The end of the interrupted run", s_runTimeout, () => second.Output.Tail()),
            async call => await lab.RunAsync(runId, call) is { Summary.State: DeploymentState.Done or DeploymentState.Failed } ended ? ended : null,
            cancellationToken);
        Assert.Equal((DeploymentState.Failed, InterruptedError), (run.Summary.State, run.Summary.Error));
        Assert.Equal(
            [(StepState.Done, null), (StepState.Failed, InterruptedError), (StepState.Pending, (string?)null)],
            run.Steps.Select(step => (step.State, step.Error)));

        // The step failed without running again, which the log says once, and the run's disk went with the failure.
        Assert.Equal(0, second.Output.Count("Downloading"));
        Assert.Equal(1, second.Output.Count($"Step {apply.Name} failed: {InterruptedError}"));
        await Eventually.WaitAsync(
            new Expectation("The removal of the dry run's disk", TimeSpan.FromMinutes(1), () => second.Output.Tail()),
            _ => Task.FromResult(!Directory.Exists(second.Root)),
            cancellationToken);

        IReadOnlyList<AuditEvent> audit = await lab.AuditAsync(runId, cancellationToken);
        Assert.Equal(["WindowsPE"], audit.Where(entry => entry.Action == "deployment.resumed").Select(ResumedIn));

        lab.AssertClean([first, second], [$"Step {apply.Name} failed: {InterruptedError}", $"The run failed: {InterruptedError}"]);
    }

    [Fact(Timeout = 600_000)]
    public async Task AModelRuleLeavesANewMachinePendingUntilAnOperatorApprovesItsSequence()
    {
        lab.SkipWhenUnavailable();
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;

        // Before a partition, Windows PE cannot restart, so the script cannot ask for it.
        SequenceView sequence = await lab.CreateSequenceAsync(
            "Chosen by a rule",
            [Script("Chosen by a rule", SequencePhase.WindowsPE, "echo chosen") with { RebootExitCodes = [] }],
            cancellationToken);
        RuleView rule = await CreateModelRuleAsync(sequence.Id, cancellationToken);

        try
        {
            await using AgentProcess agent = lab.StartAgent();
            MachineSummary machine = await lab.WaitForMachineAsync(agent, cancellationToken);

            // The rule chose the sequence, but nothing runs and nothing is authorized until an operator approves.
            Assert.Equal((MachineState.Pending, null), (machine.State, machine.Deployment));
            MachineSequenceResolution resolution = await lab.Api.GetAsync(
                $"api/machines/{machine.Id:D}/sequence",
                DdtJsonContext.Default.MachineSequenceResolution,
                cancellationToken);
            Assert.Equal((SequenceResolutionSource.Rule, sequence.Id, rule.Id), (resolution.Source, resolution.SequenceId, resolution.RuleId));
            Assert.Empty(await lab.RunsAsync(machine.Id, cancellationToken));

            MachineSummary approved = await lab.ApproveAsync(machine.Id, sequence.Id, cancellationToken);
            Assert.Equal(
                (MachineState.Approved, DeploymentState.Assigned, DeploymentSource.Rule),
                (approved.State, approved.Deployment!.State, approved.Deployment.Source));

            Assert.Equal(Deployed, await agent.WaitForExitAsync(s_runTimeout, cancellationToken));
            Assert.False(Directory.Exists(agent.Root), $"The dry run's disk {agent.Root} is still there.");
            Assert.Equal(1, agent.Output.Count("waiting to be authorized"));

            DeploymentView run = await lab.RunAsync(approved.Deployment.Id, cancellationToken);
            Assert.Equal(
                (DeploymentState.Done, DeploymentSource.Rule, rule.Id, "admin"),
                (run.Summary.State, run.Summary.Source, run.RuleId, run.Summary.RequestedBy));

            lab.AssertClean([agent], []);
        }
        finally
        {
            await lab.Api.DeleteAsync($"api/rules/{rule.Id:D}", HttpStatusCode.OK, CancellationToken.None);
        }
    }

    // Quick steps change the run many times a second. Their reports, and the ones the agent sends at once before it
    // reads the answer file and the join account, stay within the calls the server takes from a machine in a minute.
    [Fact(Timeout = 600_000)]
    public async Task ARunOfQuickStepsStaysWithinTheCallsTheServerTakesFromAMachine()
    {
        lab.SkipWhenUnavailable();
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;

        SequenceView sequence = await lab.CreateSequenceAsync(
            "Quick steps",
            [
                Partition(),
                .. Enumerable.Range(1, 40).Select(number => Script($"Quick step {number} in Windows PE", SequencePhase.WindowsPE, "echo quick")),
                new ApplyImageStep { Id = Guid.CreateVersion7(), Name = "Apply the image", ImageId = lab.Image.Id },
                new WriteUnattendStep { Id = Guid.CreateVersion7(), Name = "Write the answer file", LocalAdministrator = true },
                .. Enumerable.Range(1, 40).Select(number => Script($"Quick step {number} in Windows", SequencePhase.Windows, "echo quick")),
                new JoinDomainStep { Id = Guid.CreateVersion7(), Name = "Join the domain" },
            ],
            cancellationToken);

        await using AgentProcess agent = lab.StartAgent();
        (_, Guid runId) = await AuthorizeAsync(agent, sequence, ComputerName(agent), cancellationToken);

        Assert.Equal(Deployed, await agent.WaitForExitAsync(s_runTimeout, cancellationToken));
        DeploymentView run = await lab.RunAsync(runId, cancellationToken);
        Assert.Equal(DeploymentState.Done, run.Summary.State);
        Assert.All(run.Steps, step => Assert.Equal(StepState.Done, step.State));

        lab.AssertClean([agent], []);
    }

    // A raw disk image that is not signed for Secure Boot, on a machine that says Secure Boot is on: the server refuses the
    // run until it is allowed, and then the dry run's disk file holds the image, both of its tables, and the seed last.
    [Fact(Timeout = 600_000)]
    public async Task ARawDiskImageNotSignedForSecureBootIsWrittenWithItsSeedOnceAllowed()
    {
        lab.SkipWhenUnavailable();
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        ImageSummary image = lab.RawImage;
        Assert.Equal((ImageKind.RawDisk, ImageBootCapability.NotSigned, "x64"), (image.Kind, image.BootCapability, image.Architecture));
        Assert.Equal("e2e-cloudimg-amd64", image.Name);

        // Windows PE cannot restart in such a sequence, so the script takes no exit code as a restart.
        RunScriptStep before = Script("Before the disk", SequencePhase.WindowsPE, "echo %DDT_PHASE%") with { RebootExitCodes = [] };
        WriteRawImageStep write = new() { Id = Guid.CreateVersion7(), Name = "Write the disk image", ImageId = image.Id };
        WriteCloudInitSeedStep seed = SeedStep();
        SequenceView sequence = await lab.CreateSequenceAsync("Install Linux", [before, write, seed], cancellationToken);
        Assert.Contains(
            sequence.Warnings,
            warning => warning.StepId == write.Id && warning.Message.StartsWith($"{image.Name} will not start with Secure Boot on.", StringComparison.Ordinal));

        await using AgentProcess agent = lab.StartAgent(secureBoot: true);
        MachineSummary machine = await lab.WaitForMachineAsync(agent, cancellationToken);
        Assert.True(machine.SecureBootEnabled);
        await lab.Live.WatchAsync(machine.Id, cancellationToken);
        await lab.ApproveAsync(machine.Id, null, cancellationToken);

        AssignSequenceRequest assignment = new(sequence.Id, ComputerName(agent));
        JsonRequest<AssignSequenceRequest> request = DryRunLab.AssignmentRequest(machine.Id, assignment);
        string refusal = await lab.Api.SendRefusedAsync(request, HttpStatusCode.BadRequest, cancellationToken);
        Assert.Contains($"{image.Name} will not start with Secure Boot on, and this machine has Secure Boot on.", refusal, StringComparison.Ordinal);
        Assert.False(File.Exists(agent.DiskPath), $"{agent.DiskPath} was written before the run was allowed.");

        AssignSequenceRequest allowed = assignment with { AllowSecureBootMismatch = true };
        DeploymentSummary assigned = (await lab.AssignAsync(machine.Id, allowed, cancellationToken)).Deployment!;

        Assert.Equal(Deployed, await agent.WaitForExitAsync(s_runTimeout, cancellationToken));
        DeploymentView run = await lab.RunAsync(assigned.Id, cancellationToken);
        Assert.Equal((DeploymentState.Done, null), (run.Summary.State, run.Summary.Error));
        Assert.True(run.AllowSecureBootMismatch);
        Assert.Equal(
            [(before.Id, StepState.Done), (write.Id, StepState.Done), (seed.Id, StepState.Done)],
            run.Steps.Select(step => (step.StepId, step.State)));
        Assert.Equal(
            [(write.Id, ArtifactKind.Image, image.Id, image.Sha256)],
            run.Artifacts.Select(artifact => (artifact.StepId, artifact.Kind, artifact.SourceId, artifact.Sha256)));
        Assert.Equal(
            1,
            agent.Output.Count($"A boot entry {image.Name} for {RawImageInspector.BootDirectory}\\{RawImageInspector.FallbackFile} on partition 1"));
        AssertTheDiskHoldsTheImageWithTheSeedLast(agent);

        IReadOnlyList<AuditEvent> audit = await lab.AuditAsync(assigned.Id, cancellationToken);
        Assert.Contains(
            audit,
            entry => entry.Detail?.Contains($"It may write {image.Name} although it will not start with Secure Boot on.", StringComparison.Ordinal) == true);

        lab.AssertClean([agent], [$"{image.Name} is not signed for Secure Boot, and this machine has Secure Boot on. The run was allowed to write it"]);
    }

    // Approved and assigned on the web, as an operator does for a machine waiting at its prompt. The machine is
    // watched first, so no push about its run is missed.
    private async Task<(Guid MachineId, Guid RunId)> AuthorizeAsync(
        AgentProcess agent,
        SequenceView sequence,
        string? computerName,
        CancellationToken cancellationToken)
    {
        MachineSummary machine = await lab.WaitForMachineAsync(agent, cancellationToken);
        await lab.Live.WatchAsync(machine.Id, cancellationToken);
        await lab.ApproveAsync(machine.Id, null, cancellationToken);
        AssignSequenceRequest assignment = new(sequence.Id, computerName);
        MachineSummary assigned = await lab.AssignAsync(machine.Id, assignment, cancellationToken);

        return (machine.Id, assigned.Deployment!.Id);
    }

    // For the dry runs' model, choosing the sequence. The test deletes it.
    private Task<RuleView> CreateModelRuleAsync(Guid sequenceId, CancellationToken cancellationToken) =>
        lab.Api.SendAsync(
            new JsonRequest<SaveRuleRequest>(
                HttpMethod.Post,
                "api/rules",
                new SaveRuleRequest(
                    0,
                    $"Model {DryRunLab.Model}",
                    "Made by the end-to-end tests.",
                    true,
                    new AllCondition
                    {
                        Parts =
                        [
                            new TestCondition(MachineVariableNames.Manufacturer, ConditionOperator.Equals, DryRunLab.Manufacturer),
                            new TestCondition(MachineVariableNames.Model, ConditionOperator.Equals, DryRunLab.Model),
                        ],
                    },
                    sequenceId,
                    [],
                    []),
                DdtJsonContext.Default.SaveRuleRequest),
            DdtJsonContext.Default.RuleView,
            HttpStatusCode.Created,
            cancellationToken);

    // Each step of the whole run ended in its phase as expected, with its times, and its end reached the page that
    // watched the machine.
    private void AssertEveryStepEndedAsExpected(DeploymentView run, WholeRunSequence steps, Guid machineId)
    {
        Assert.Equal(
            [
                (steps.Partition.Id, SequencePhase.WindowsPE, StepState.Done, null),
                (steps.Phase.Id, SequencePhase.WindowsPE, StepState.Done, null),
                (steps.Skipped.Id, SequencePhase.WindowsPE, StepState.Skipped, null),
                (steps.Failing.Id, SequencePhase.WindowsPE, StepState.Failed, "The script ended with exit code 0, which is not one of its success codes (1)."),
                (steps.RestartWindowsPE.Id, SequencePhase.WindowsPE, StepState.Done, null),
                (steps.AfterRestart.Id, SequencePhase.WindowsPE, StepState.Done, null),
                (steps.Apply.Id, SequencePhase.WindowsPE, StepState.Done, null),
                (steps.Drivers.Id, SequencePhase.WindowsPE, StepState.Done, null),
                (steps.Unattend.Id, SequencePhase.WindowsPE, StepState.Done, null),
                (steps.PowerShell.Id, SequencePhase.Windows, StepState.Done, null),
                (steps.RestartWindows.Id, SequencePhase.Windows, StepState.Done, null),
                (steps.Packaged.Id, SequencePhase.Windows, StepState.Done, null),
                (steps.Join.Id, SequencePhase.Windows, StepState.Done, (string?)null),
            ],
            run.Steps.Select(step => (step.StepId, step.Phase, step.State, step.Error)));
        Assert.All(
            run.Steps.Where(step => step.State != StepState.Skipped),
            step => Assert.True(step.StartedUtc <= step.FinishedUtc, $"{step.Name} has no times."));
        Assert.All(run.Steps, step => Assert.Contains(lab.Live.StepPushes(machineId), pushed => (pushed.StepId, pushed.State) == (step.StepId, step.State)));
    }

    // The run keeps the sequence's revision, definition and artifacts it was assigned, whatever happens to the sequence
    // later.
    private async Task AssertTheRunKeepsWhatItWasAssignedAsync(
        SequenceView sequence,
        DeploymentView run,
        WholeRunSequence steps,
        Guid runId,
        CancellationToken cancellationToken)
    {
        Assert.Equal(sequence.Revision, run.SequenceRevision);
        Assert.Equal(Json(sequence.Definition), Json(run.Definition!));
        Assert.Equal(
            [
                (steps.Apply.Id, ArtifactKind.Image, lab.Image.Id, lab.Image.Sha256),
                (steps.Drivers.Id, ArtifactKind.Drivers, lab.Drivers.Id, lab.Drivers.Sha256),
                (steps.Packaged.Id, ArtifactKind.Files, lab.Files.Id, lab.Files.Sha256),
            ],
            run.Artifacts.OrderBy(artifact => artifact.Kind).Select(artifact => (artifact.StepId, artifact.Kind, artifact.SourceId, artifact.Sha256)));

        SequenceDefinition renamed = sequence.Definition with
        {
            Steps = [steps.Partition with { Name = "Renamed after the run" }, .. sequence.Definition.Steps.Skip(1)],
        };
        await lab.Api.SendAsync(
            new JsonRequest<SaveSequenceRequest>(
                HttpMethod.Put,
                $"api/sequences/{sequence.Id:D}",
                new SaveSequenceRequest(sequence.Revision, sequence.Name, sequence.Description, renamed),
                DdtJsonContext.Default.SaveSequenceRequest),
            DdtJsonContext.Default.SequenceView,
            HttpStatusCode.OK,
            cancellationToken);
        Assert.Equal(Json(sequence.Definition), Json((await lab.RunAsync(runId, cancellationToken)).Definition!));
    }

    // The log before the run, the run's own lines, pages back and forth, and the pushes that announced them.
    private async Task<MachineLogEntry[]> CheckLogAsync(
        Guid machineId,
        Guid runId,
        Guid applyStepId,
        MachineLogAppendedEvent firstPush,
        CancellationToken cancellationToken)
    {
        MachineLogPage all = await lab.LogAsync(machineId, "limit=1000", cancellationToken);
        MachineLogEntry[] lines = [.. all.Lines];
        Assert.False(all.HasOlder);
        Assert.True(lines.Length > 40, $"The machine's log has only {lines.Length} lines.");
        Assert.Contains(lines, line => line.DeploymentId is null);

        MachineLogPage ofRun = await lab.LogAsync(machineId, $"deploymentId={runId:D}&limit=1000", cancellationToken);
        Assert.Equal(lines.Where(line => line.DeploymentId == runId).Select(line => line.Id), ofRun.Lines.Select(line => line.Id));
        Assert.InRange(ofRun.Lines.Count, 1, lines.Length - 1);
        Assert.Contains(ofRun.Lines, line => line.StepId == applyStepId);

        MachineLogPage newest = await lab.LogAsync(machineId, "limit=10", cancellationToken);
        Assert.True(newest.HasOlder);
        Assert.Equal(lines[^10..].Select(line => line.Id), newest.Lines.Select(line => line.Id));
        MachineLogPage older = await lab.LogAsync(machineId, $"before={newest.Lines[0].Id}&limit=10", cancellationToken);
        Assert.Equal(lines[^20..^10].Select(line => line.Id), older.Lines.Select(line => line.Id));
        MachineLogPage later = await lab.LogAsync(machineId, $"after={lines[4].Id}&limit=10", cancellationToken);
        Assert.Equal(lines[5..15].Select(line => line.Id), later.Lines.Select(line => line.Id));

        // The first push came before the run, and the last one names the last line.
        Assert.Contains(lines, line => line.Id == firstPush.LastLineId && line.DeploymentId is null);
        await Eventually.WaitAsync(
            new Expectation(
                "A push for the machine's last line",
                TimeSpan.FromSeconds(10),
                () => string.Join(", ", lab.Live.LogPushes(machineId).Select(push => push.LastLineId))),
            _ => Task.FromResult(lab.Live.LogPushes(machineId)[^1].LastLineId == lines[^1].Id),
            cancellationToken);
        long[] pushed = [.. lab.Live.LogPushes(machineId).Select(push => push.LastLineId)];
        Assert.Equal(pushed.Order(), pushed);

        return lines;
    }

    // Once, although the run went on after several restarts. The step never ran, so the line is the run's.
    private static void AssertTheSkipIsLoggedOnce(MachineLogEntry[] lines, RunScriptStep skipped)
    {
        MachineLogEntry skip = Assert.Single(lines, line => line.Message.StartsWith($"Step {skipped.Name} was skipped", StringComparison.Ordinal));
        Assert.Equal(
            ($"Step {skipped.Name} was skipped, because this condition did not hold: Model is \"Another model\", and the machine reports \"Dry run\".", (Guid?)null),
            (skip.Message, skip.StepId));
    }

    // The image with both of its tables, and the seed in the last partition, well after the image's.
    private static void AssertTheDiskHoldsTheImageWithTheSeedLast(AgentProcess agent)
    {
        using FileStream disk = new(agent.DiskPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        byte[] head = new byte[GptLayout.MaxHeadBytes];
        disk.ReadExactly(head);
        GptLayout layout = GptLayout.Read(head);
        Assert.Equal((disk.Length / GptLayout.SectorSize) - 1, layout.BackupLba);

        byte[] backup = new byte[GptLayout.SectorSize];
        disk.Position = layout.BackupLba * GptLayout.SectorSize;
        disk.ReadExactly(backup);
        Assert.Equal(layout.BackupHeader(), backup);

        Assert.Equal(["EFI", "root", CloudInitSeed.Label], layout.Partitions.Select(partition => partition.Name));
        GptPartition seeded = layout.Partitions[^1];
        Assert.True(seeded.FirstLba > layout.Partitions[1].LastLba + 2048, "The seed is not at the end of the disk.");

        FatVolume volume = FatVolume.Open(disk, seeded.FirstLba * GptLayout.SectorSize, seeded.Sectors * GptLayout.SectorSize);
        Assert.Equal(CloudInitSeed.Label, volume.Label);
        Assert.Contains($"local-hostname: \"{ComputerName(agent)}\"", Text(volume, CloudInitSeed.MetaData), StringComparison.Ordinal);
        Assert.Contains($"content: \"{agent.SerialNumber}\"", Text(volume, CloudInitSeed.UserData), StringComparison.Ordinal);
        Assert.Null(volume.Find(CloudInitSeed.NetworkConfig));
    }

    // Names the machine and writes its serial number, which AssertTheDiskHoldsTheImageWithTheSeedLast looks for.
    private static WriteCloudInitSeedStep SeedStep() => new()
    {
        Id = Guid.CreateVersion7(),
        Name = "Write the cloud-init seed",
        MetaData = "instance-id: \"{{SmbiosUuid}}\"\nlocal-hostname: \"{{ComputerName}}\"\n",
        UserData = "#cloud-config\nwrite_files:\n  - path: /etc/ddt-serial\n    content: \"{{SerialNumber}}\"\n",
    };

    // In the dry run's terms, as it only logs what the removal would change: the service deleted, and what is left of
    // the agent marked for deletion when Windows next starts. The dry run's disk goes whatever the removal did.
    internal static void AssertRemovedItself(AgentProcess agent)
    {
        Assert.Equal(1, agent.Output.Count("The run is over here, so the agent removes itself."));
        Assert.Equal(1, agent.Output.Count($"Dry run: not run: {Path.Combine(Environment.SystemDirectory, "sc.exe")} delete DdtSequence"));
        Assert.Equal(1, agent.Output.Count($"Dry run: in Windows, {Path.Combine(agent.Root, "W", "DDT")} would be marked for deletion when Windows next starts."));
        Assert.False(Directory.Exists(agent.Root), $"The dry run's disk {agent.Root} is still there.");
    }

    // A sequence that joins the domain needs one.
    private static string ComputerName(AgentProcess agent) => string.Create(CultureInfo.InvariantCulture, $"E2E-{agent.DryRunId % 100_000:D5}");

    internal static PartitionStep Partition() => new() { Id = Guid.CreateVersion7(), Name = "Partition" };

    internal static RunScriptStep Script(string name, SequencePhase phase, string script, ScriptInterpreter interpreter = ScriptInterpreter.Cmd) =>
        new() { Id = Guid.CreateVersion7(), Name = name, Phase = phase, Interpreter = interpreter, Script = script };

    private static string Text(FatVolume volume, string name) => Encoding.UTF8.GetString(volume.ReadFile(volume.Find(name)!, 64 * 1024));

    private static string Json(SequenceDefinition definition) => JsonSerializer.Serialize(definition, DdtJsonContext.Default.SequenceDefinition);

    // Where the agent went on, from "Continued <title> (<run>) from Windows with its run token.".
    internal static string ResumedIn(AuditEvent resumed) =>
        resumed.Detail switch
        {
            { } detail when detail.Contains(" from WindowsPE ", StringComparison.Ordinal) => "WindowsPE",
            { } detail when detail.Contains(" from Windows ", StringComparison.Ordinal) => "Windows",
            var detail => detail ?? string.Empty,
        };
}
