// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Net;
using System.Text;
using System.Text.Json;
using DDT.Contracts;
using DDT.Contracts.Accounts;
using DDT.Contracts.Agents;
using DDT.Contracts.Deployments;
using DDT.Contracts.Machines;
using DDT.Contracts.Rules;
using DDT.Contracts.Sequences;
using DDT.Contracts.Values;
using DDT.Core.CloudInit;
using DDT.Core.Disks;
using DDT.Core.Sequences;
using DDT.Server.Data;
using Xunit;

namespace DDT.E2E;

// Dry runs of what version 3 of a sequence adds: IFs, repeats, pauses, variables, inputs and a stored account's shares,
// and of the values rules give a run, flat or not.
[Trait("Category", "E2E")]
[Collection(DryRunCollection.Name)]
public sealed class TreeDryRunTests(DryRunLab lab)
{
    // The agent's exit code for a run that finished.
    private const int Deployed = 4;

    private static readonly TimeSpan s_runTimeout = TimeSpan.FromMinutes(3);

    [Fact(Timeout = 600_000)]
    public async Task ATreeTakesItsBranchTriesAgainAfterARestartAndPausesInWindowsUntilTheWebContinuesIt()
    {
        lab.SkipWhenUnavailable();
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;

        AccountView account = await lab.CreateAccountAsync(
            TreeSequence.ShareUser,
            DryRunLab.Domain,
            [TreeSequence.ShareHost],
            runAs: true,
            cancellationToken);
        TreeSequence tree = new(lab, account);
        SequenceView sequence = await lab.CreateSequenceAsync("Tree", tree.Definition, cancellationToken);
        Assert.Equal(SequenceDefinition.CurrentVersion, sequence.Definition.Version);

        await using AgentProcess agent = lab.StartAgent();
        (Guid machineId, Guid runId) = await AuthorizeAsync(agent, sequence, null, cancellationToken);
        string labName = $"LAB-{agent.SerialNumber[^5..]}";

        // The run waits at the pause in Windows with its message worked out, and nothing after it runs meanwhile.
        DeploymentView paused = await WaitForRunAsync(
            agent,
            runId,
            run => run.Summary is { Waiting: true, Activity: RunActivity.Paused },
            "The pause in Windows",
            cancellationToken);
        string message = $"Check {labName} before the drivers are copied.";
        Assert.Equal((DeploymentState.Running, SequencePhase.Windows, message), (paused.Summary.State, paused.Summary.Phase, paused.Summary.PauseMessage));
        Assert.Equal((tree.Pause.Id, 1, message, (DateTimeOffset?)null), (paused.Pause!.StepId, paused.Pause.Pass, paused.Pause.Message, paused.Pause.ContinuesUtc));
        Assert.Equal((StepState.Running, StepState.Pending), (Row(paused, tree.Pause.Id).State, Row(paused, tree.Copy.Id).State));

        DeploymentView continued = await lab.ContinueAsync(machineId, paused.Pause, cancellationToken);
        Assert.Equal((false, null, null), (continued.Summary.Waiting, continued.Summary.PauseMessage, continued.Pause));

        Assert.Equal(Deployed, await agent.WaitForExitAsync(s_runTimeout, cancellationToken));
        DryRunTests.AssertRemovedItself(agent);
        Assert.Equal(1, agent.Output.Count("Someone continued the run on the web."));

        DeploymentView run = await lab.RunAsync(runId, cancellationToken);
        Assert.Equal((DeploymentState.Done, null, false), (run.Summary.State, run.Summary.Error, run.Summary.Waiting));
        AssertTheTreeWasWalkedAsExpected(run, tree, machineId);
        AssertOnlyTheBranchTakenAndTheSecondTryRan(agent, tree);
        AssertTheBranchesNameReachedTheRun(run, agent, labName, machineId);
        AssertTheScriptRanAsTheStoredAccount(agent);
        IReadOnlyList<AuditEvent> audit = await AssertTheAuditOfTheTreeAsync(runId, sequence, tree, cancellationToken);

        MachineLogPage log = await lab.LogAsync(machineId, $"deploymentId={runId:D}&limit=1000", cancellationToken);
        lab.AssertClean(
            [agent],
            [$"Step {tree.FirstTry.Name} failed after"],
            ("The paused run's detail", JsonSerializer.Serialize(paused, DdtJsonContext.Default.DeploymentView)),
            ("The run's detail", JsonSerializer.Serialize(run, DdtJsonContext.Default.DeploymentView)),
            ("The run's audit", string.Join(Environment.NewLine, audit.Select(entry => entry.Detail))),
            ("The run's log", string.Join(Environment.NewLine, log.Lines.Select(line => line.Message))));
    }

    // A rule for the dry run's network gives a flat sequence's run a time zone, a site and a computer name made from the
    // site, which the run keeps with where each came from, and the answer file the agent fetched holds them.
    [Fact(Timeout = 600_000)]
    public async Task ARuleForTheMachinesNetworkGivesItsRunValuesThatTheAnswerFileHolds()
    {
        lab.SkipWhenUnavailable();
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;

        // A list of steps without anything of version 3, which older agents run as well.
        SequenceView sequence = await lab.CreateSequenceAsync(
            "Values from a rule",
            [
                DryRunTests.Partition(),
                new ApplyImageStep { Id = Guid.CreateVersion7(), Name = "Apply the image", ImageId = lab.Image.Id },
                new WriteUnattendStep { Id = Guid.CreateVersion7(), Name = "Write the answer file" },
            ],
            cancellationToken);
        Assert.True(sequence.Definition.Version < SequenceDefinition.CurrentVersion, $"The list is stored as version {sequence.Definition.Version}.");

        RuleView rule = await CreateNetworkRuleAsync(
            [new NamedValue("TimeZone", "Pacific Standard Time"), new NamedValue("Site", "SEA"), new NamedValue(MachineVariableNames.ComputerName, "{{Site}}-{{SerialNumber|right:5}}")],
            cancellationToken);

        try
        {
            await using AgentProcess agent = lab.StartAgent();
            (_, Guid runId) = await AuthorizeAsync(agent, sequence, null, cancellationToken);

            Assert.Equal(Deployed, await agent.WaitForExitAsync(s_runTimeout, cancellationToken));
            DeploymentView run = await lab.RunAsync(runId, cancellationToken);
            Assert.Equal((DeploymentState.Done, null), (run.Summary.State, run.Summary.Error));

            // The rule's values win over the deployment defaults, which the run shows as overridden.
            string computerName = $"SEA-{agent.SerialNumber[^5..]}";
            Assert.Equal(
                [
                    ("TimeZone", "Pacific Standard Time", ValueSource.Rule, rule.Id, rule.Name, false),
                    ("TimeZone", "W. Europe Standard Time", ValueSource.DeploymentDefault, null, null, true),
                    ("Site", "SEA", ValueSource.Rule, rule.Id, rule.Name, false),
                    (MachineVariableNames.ComputerName, computerName, ValueSource.Rule, rule.Id, rule.Name, false),
                ],
                run.Values!
                    .Where(value => value.Name is "TimeZone" or "Site" or MachineVariableNames.ComputerName)
                    .Select(value => (value.Name, value.Value, value.Source, value.SourceId, value.SourceName, value.Overridden)));
            Assert.Equal(1, agent.Output.Count($"Wrote the answer file: computer name {computerName}, time zone Pacific Standard Time, "));

            IReadOnlyList<AuditEvent> audit = await lab.AuditAsync(runId, cancellationToken);
            Assert.StartsWith(
                "The answer file of step Write the answer file",
                Assert.Single(audit, entry => entry.Action == "deployment.secret-read").Detail,
                StringComparison.Ordinal);

            lab.AssertClean([agent], [], ("The run's detail", JsonSerializer.Serialize(run, DdtJsonContext.Default.DeploymentView)));
        }
        finally
        {
            await lab.Api.DeleteAsync($"api/rules/{rule.Id:D}", HttpStatusCode.OK, CancellationToken.None);
        }
    }

    // A raw disk image's cloud-init seed names the machine as a rule's computer name pattern does, for a run assigned
    // without a name.
    [Fact(Timeout = 600_000)]
    public async Task ARawDiskImagesSeedNamesTheMachineAsARulesPatternDoes()
    {
        lab.SkipWhenUnavailable();
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;

        SequenceView sequence = await lab.CreateSequenceAsync(
            "Named by a rule",
            [
                new WriteRawImageStep { Id = Guid.CreateVersion7(), Name = "Write the disk image", ImageId = lab.RawImage.Id },
                new WriteCloudInitSeedStep
                {
                    Id = Guid.CreateVersion7(),
                    Name = "Write the cloud-init seed",
                    MetaData = "local-hostname: \"{{ComputerName}}\"\n",
                    UserData = "#cloud-config\n",
                },
            ],
            cancellationToken);
        RuleView rule = await CreateNetworkRuleAsync([new NamedValue(MachineVariableNames.ComputerName, "LNX-{{SerialNumber|right:5}}")], cancellationToken);

        try
        {
            await using AgentProcess agent = lab.StartAgent();
            (_, Guid runId) = await AuthorizeAsync(agent, sequence, null, cancellationToken);

            Assert.Equal(Deployed, await agent.WaitForExitAsync(s_runTimeout, cancellationToken));
            DeploymentView run = await lab.RunAsync(runId, cancellationToken);
            Assert.Equal((DeploymentState.Done, null), (run.Summary.State, run.Summary.Error));

            string computerName = $"LNX-{agent.SerialNumber[^5..]}";
            Assert.Equal(
                (computerName, ValueSource.Rule, rule.Id),
                run.Values!.Where(value => value.Name == MachineVariableNames.ComputerName).Select(value => (value.Value, value.Source, value.SourceId)).Single());

            using FileStream disk = new(agent.DiskPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            byte[] head = new byte[GptLayout.MaxHeadBytes];
            disk.ReadExactly(head);
            GptPartition seed = GptLayout.Read(head).Partitions[^1];
            FatVolume volume = FatVolume.Open(disk, seed.FirstLba * GptLayout.SectorSize, seed.Sectors * GptLayout.SectorSize);
            Assert.Equal(CloudInitSeed.Label, volume.Label);
            Assert.Equal(
                $"local-hostname: \"{computerName}\"\n",
                Encoding.UTF8.GetString(volume.ReadFile(volume.Find(CloudInitSeed.MetaData)!, 64 * 1024)));

            lab.AssertClean([agent], [], ("The run's detail", JsonSerializer.Serialize(run, DdtJsonContext.Default.DeploymentView)));
        }
        finally
        {
            await lab.Api.DeleteAsync($"api/rules/{rule.Id:D}", HttpStatusCode.OK, CancellationToken.None);
        }
    }

    // An input asked on the web, answered with the assignment, and a required one the machine asks: nobody can answer at
    // the dry run's machine, so the run waits at its start until the machine's page answers it, then starts with both.
    [Fact(Timeout = 600_000)]
    public async Task ARunWaitsAtItsStartForAnInputTheMachineAsksUntilTheWebAnswersIt()
    {
        lab.SkipWhenUnavailable();
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;

        InputDeclaration owner = new() { Name = "Owner", Label = "Owner", Required = true, AskAt = InputAsk.Web };
        InputDeclaration room = new() { Name = "Room", Label = "Room", Required = true, AskAt = InputAsk.Machine };
        SetVariableStep label = new() { Id = Guid.CreateVersion7(), Name = "Label the PC", Variable = "Label", Value = "{{Owner}}, room {{Room}}" };

        // Before a partition, Windows PE cannot restart, so the script cannot ask for it.
        RunScriptStep inRoom = DryRunTests.Script("Only in room B12", SequencePhase.WindowsPE, "echo %DDT_VAR_Room%") with
        {
            RebootExitCodes = [],
            When = new TestCondition(room.Name, ConditionOperator.Equals, "B12"),
        };
        SequenceView sequence = await lab.CreateSequenceAsync(
            "Inputs",
            new SequenceDefinition(SequenceDefinition.CurrentVersion, [label, inRoom])
            {
                Variables = [new VariableDeclaration { Name = "Label", SetBySteps = true }],
                Inputs = [owner, room],
            },
            cancellationToken);

        await using AgentProcess agent = lab.StartAgent();
        (Guid machineId, Guid runId) = await AuthorizeAsync(agent, sequence, [new InputAnswer(owner.Name, "Jane Roe")], cancellationToken);

        DeploymentView waiting = await WaitForRunAsync(
            agent,
            runId,
            run => run.Summary is { Waiting: true, Activity: RunActivity.WaitingForInput },
            "The wait for the room",
            cancellationToken);
        Assert.Equal((DeploymentState.Assigned, null), (waiting.Summary.State, waiting.Values));
        Assert.Equal([(owner.Name, true, "admin"), (room.Name, false, null)], waiting.Inputs!.Select(input => (input.Input.Name, input.Answered, input.AnsweredBy)));
        Assert.All(waiting.Steps, step => Assert.Equal(StepState.Pending, step.State));

        DeploymentView answered = await lab.AnswerAsync(machineId, [new InputAnswer(room.Name, "B12")], cancellationToken);
        Assert.False(answered.Summary.Waiting);
        Assert.Equal([(owner.Name, true, "admin"), (room.Name, true, "admin")], answered.Inputs!.Select(input => (input.Input.Name, input.Answered, input.AnsweredBy)));

        Assert.Equal(Deployed, await agent.WaitForExitAsync(s_runTimeout, cancellationToken));
        Assert.Equal(1, agent.Output.Count("The run waits for answers to Room, at this machine or on the machine's page."));
        Assert.Equal(1, agent.Output.Count("The inputs were answered on the web, and the run starts."));

        // Both answers are the run's values, and its steps worked with them.
        DeploymentView run = await lab.RunAsync(runId, cancellationToken);
        Assert.Equal((DeploymentState.Done, null), (run.Summary.State, run.Summary.Error));
        Assert.Equal([(label.Id, StepState.Done), (inRoom.Id, StepState.Done)], run.Steps.Select(step => (step.StepId, step.State)));
        Assert.Equal(
            [(owner.Name, "Jane Roe", ValueSource.Input, false), (room.Name, "B12", ValueSource.Input, false)],
            run.Values!.Where(value => value.Name is "Owner" or "Room").Select(value => (value.Name, value.Value, value.Source, value.Overridden)));
        Assert.Equal("Jane Roe, room B12", run.Variables!["Label"]);

        IReadOnlyList<AuditEvent> audit = await lab.AuditAsync(runId, cancellationToken);
        Assert.Contains(audit, entry => entry is { Action: "deployment.inputs-answered", ActorName: "admin" });

        lab.AssertClean([agent], [], ("The run's detail", JsonSerializer.Serialize(run, DdtJsonContext.Default.DeploymentView)));
    }

    // A row per node in the tree's order, with the latest visit of each: the branch the IF took and the one it skipped,
    // the repeat's second time through, whose first try and restart were skipped, and the steps after it.
    private void AssertTheTreeWasWalkedAsExpected(DeploymentView run, TreeSequence tree, Guid machineId)
    {
        Assert.Equal(
            [
                (tree.Partition.Id, null, 0, SequencePhase.WindowsPE, StepState.Done, 1, 0, null),
                (tree.ByModel.Id, null, 0, SequencePhase.WindowsPE, StepState.Done, 1, 0, IfBranch.Then),
                (tree.ApplyLab.Id, tree.ByModel.Id, 1, SequencePhase.WindowsPE, StepState.Done, 1, 0, null),
                (tree.NameForLab.Id, tree.ByModel.Id, 1, SequencePhase.WindowsPE, StepState.Done, 1, 0, null),
                (tree.ApplyOther.Id, tree.ByModel.Id, 1, SequencePhase.WindowsPE, StepState.Skipped, 1, 0, null),
                (tree.UntilInstalled.Id, null, 0, SequencePhase.WindowsPE, StepState.Done, 1, 2, null),
                (tree.Count.Id, tree.UntilInstalled.Id, 1, SequencePhase.WindowsPE, StepState.Done, 2, 0, null),
                (tree.Restart.Id, tree.UntilInstalled.Id, 1, SequencePhase.WindowsPE, StepState.Skipped, 2, 0, null),
                (tree.FirstTry.Id, tree.UntilInstalled.Id, 1, SequencePhase.WindowsPE, StepState.Skipped, 2, 0, null),
                (tree.SecondTry.Id, tree.UntilInstalled.Id, 1, SequencePhase.WindowsPE, StepState.Done, 2, 0, null),
                (tree.Unattend.Id, null, 0, SequencePhase.WindowsPE, StepState.Done, 1, 0, null),
                (tree.InWindows.Id, null, 0, SequencePhase.Windows, StepState.Done, 1, 0, null),
                (tree.Pause.Id, null, 0, SequencePhase.Windows, StepState.Done, 1, 0, null),
                (tree.Copy.Id, null, 0, SequencePhase.Windows, StepState.Done, 1, 0, (IfBranch?)null),
            ],
            run.Steps.Select(step => (step.StepId, step.ParentId, step.Depth, step.Phase, step.State, step.Pass, step.Iteration, step.Branch)));

        // What each decision tested, and the value it found.
        Assert.Equal([new TestEvaluation(ConditionEvaluator.TestPath, true, DryRunLab.Model)], Row(run, tree.ByModel.Id).Evaluation);
        Assert.Equal(
            [new TestEvaluation($"{ConditionEvaluator.UntilPath}.parts[0]", true, "No"), new TestEvaluation($"{ConditionEvaluator.UntilPath}.parts[1]", true, "0")],
            Row(run, tree.UntilInstalled.Id).Evaluation);
        Assert.Equal([new TestEvaluation(ConditionEvaluator.WhenPath, false, "0+1+1")], Row(run, tree.FirstTry.Id).Evaluation);
        Assert.Equal([new TestEvaluation(ConditionEvaluator.WhenPath, true, "0+1+1")], Row(run, tree.SecondTry.Id).Evaluation);
        Assert.All(run.Steps.Where(step => step.State == StepState.Done), step => Assert.True(step.StartedUtc <= step.FinishedUtc, $"{step.Name} has no times."));
        Assert.All(run.Steps, step => Assert.Contains(lab.Live.StepPushes(machineId), pushed => (pushed.StepId, pushed.State, pushed.Pass) == (step.StepId, step.State, step.Pass)));
    }

    // The first try failed, the repeat went through its steps again after it, and the second try ended it. Only the
    // image of the branch the IF took was applied.
    private void AssertOnlyTheBranchTakenAndTheSecondTryRan(AgentProcess agent, TreeSequence tree)
    {
        Assert.Equal(1, agent.Output.Count($"Step {tree.FirstTry.Name} failed after"));
        Assert.Equal(1, agent.Output.Count($"Repeat {tree.UntilInstalled.Name} runs its steps again (2 of at most 3 times)"));

        Assert.Equal(1, agent.Output.Count($"Applying image 1 of {lab.Image.Name} to "));
        Assert.Equal(0, agent.Output.Count(lab.OtherImage.Name));
    }

    // The name the IF's branch set went into the answer file and the pause's message, and it and the count of tries
    // reached the run and the page that watched the machine; the values the run started with stay as they were.
    private void AssertTheBranchesNameReachedTheRun(DeploymentView run, AgentProcess agent, string labName, Guid machineId)
    {
        Assert.Equal((labName, "0+1+1"), (run.Variables![MachineVariableNames.ComputerName], run.Variables[TreeSequence.Tries]));
        Assert.Contains(lab.Live.VariablePushes(machineId), pushed => pushed.GetValueOrDefault(MachineVariableNames.ComputerName) == labName);
        Assert.Equal(
            [($"PC-{agent.SerialNumber[^5..]}", ValueSource.SequenceDefault), ("0", ValueSource.SequenceDefault)],
            run.Values!.Where(value => value.Name is MachineVariableNames.ComputerName or TreeSequence.Tries).Select(value => (value.Value, value.Source)));
        Assert.Equal(1, agent.Output.Count($"Wrote the answer file: computer name {labName}, time zone W. Europe Standard Time, "));
    }

    // With its share, which the dry run only names, from what the server handed out for the step while it ran.
    private static void AssertTheScriptRanAsTheStoredAccount(AgentProcess agent)
    {
        Assert.Equal(1, agent.Output.Count($"Dry run: {TreeSequence.ShareUser} is not signed in."));
        Assert.Equal(1, agent.Output.Count($@"Dry run: not connected: \\{TreeSequence.ShareHost}\drivers as {TreeSequence.ShareUser}, for {TreeSequence.ShareUser}."));
        Assert.Equal(1, agent.Output.Count($"Dry run: not run as {TreeSequence.ShareUser} "));
    }

    // Restarted once in Windows PE and handed over once; the answer file and the step's account were read once for
    // what each was for, and the pause was continued by the administrator.
    private async Task<IReadOnlyList<AuditEvent>> AssertTheAuditOfTheTreeAsync(
        Guid runId,
        SequenceView sequence,
        TreeSequence tree,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<AuditEvent> audit = await lab.AuditAsync(runId, cancellationToken);
        Assert.Equal(["WindowsPE", "Windows"], audit.Where(entry => entry.Action == "deployment.resumed").OrderBy(entry => entry.Id).Select(DryRunTests.ResumedIn));
        string[] reads = [.. audit.Where(entry => entry.Action == "deployment.secret-read").OrderBy(entry => entry.Id).Select(entry => entry.Detail ?? "")];
        string named = $"The account {tree.Account.Name} ({tree.Account.Id:D}) of step {tree.Copy.Name} ({tree.Copy.Id:D}) of {sequence.Name}";
        Assert.Equal(3, reads.Length);
        Assert.StartsWith($"The answer file of step {tree.Unattend.Name} ({tree.Unattend.Id:D})", reads[0], StringComparison.Ordinal);
        Assert.Equal([$"{named}, to run the script as {TreeSequence.ShareUser}.", $@"{named}, for \\{TreeSequence.ShareHost}\drivers."], reads[1..]);
        AuditEvent continuedBy = Assert.Single(audit, entry => entry.Action == "deployment.continued");
        Assert.Equal("admin", continuedBy.ActorName);

        return audit;
    }

    // Approved and assigned on the web, with the answers to the inputs asked there, the machine watched first so no push
    // about its run is missed.
    private async Task<(Guid MachineId, Guid RunId)> AuthorizeAsync(
        AgentProcess agent,
        SequenceView sequence,
        IReadOnlyList<InputAnswer>? answers,
        CancellationToken cancellationToken)
    {
        MachineSummary machine = await lab.WaitForMachineAsync(agent, cancellationToken);
        await lab.Live.WatchAsync(machine.Id, cancellationToken);
        await lab.ApproveAsync(machine.Id, null, cancellationToken);
        AssignSequenceRequest assignment = new(sequence.Id, null, Answers: answers);
        MachineSummary assigned = await lab.AssignAsync(machine.Id, assignment, cancellationToken);

        return (machine.Id, assigned.Deployment!.Id);
    }

    // Until the run is as wanted, failing at once should it end otherwise.
    private Task<DeploymentView> WaitForRunAsync(
        AgentProcess agent,
        Guid runId,
        Func<DeploymentView, bool> wanted,
        string what,
        CancellationToken cancellationToken) =>
        Eventually.GetAsync(
            new Expectation(what, s_runTimeout, () => agent.Output.Tail()),
            async call =>
            {
                DeploymentView run = await lab.RunAsync(runId, call);

                if (wanted(run))
                {
                    return run;
                }

                return run.Summary.State is DeploymentState.Done or DeploymentState.Failed or DeploymentState.Cancelled
                    ? throw new InvalidOperationException(
                        $"{what} did not happen: the run ended {run.Summary.State} ({run.Summary.Error}).{Environment.NewLine}{agent.Output.Tail()}")
                    : null;
            },
            cancellationToken);

    // A rule for the dry runs' network, which every dry run's machine is on, that gives these values and chooses no
    // sequence. The test deletes it.
    private async Task<RuleView> CreateNetworkRuleAsync(IReadOnlyList<NamedValue> values, CancellationToken cancellationToken)
    {
        RuleView rule = await lab.Api.SendAsync(
            new JsonRequest<SaveRuleRequest>(
                HttpMethod.Post,
                "api/rules",
                new SaveRuleRequest(
                    0,
                    $"The dry runs' network {Guid.NewGuid():N}",
                    "Made by the end-to-end tests.",
                    true,
                    new TestCondition(MachineVariableNames.IPv4Address, ConditionOperator.InSubnet, "192.0.2.0/24"),
                    null,
                    values,
                    []),
                DdtJsonContext.Default.SaveRuleRequest),
            DdtJsonContext.Default.RuleView,
            HttpStatusCode.Created,
            cancellationToken);
        Assert.Empty(rule.Problems);

        return rule;
    }

    private static DeploymentStepView Row(DeploymentView run, Guid stepId) => run.Steps.Single(step => step.StepId == stepId);
}
