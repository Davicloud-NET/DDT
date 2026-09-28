// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Net;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;
using DDT.Agent.Consoles;
using DDT.ConsoleProtocol;
using DDT.Contracts.Agents;
using DDT.Contracts.Deployments;
using DDT.Contracts.Images;
using DDT.Contracts.Machines;
using DDT.Contracts.Sequences;
using Xunit;
using static DDT.Agent.Tests.ScriptedMachineConsole;

namespace DDT.Agent.Tests;

// The agent's flows as a console other than the text console sees them: questions with their facts and the last error,
// answers that name a choice, questions withdrawn when the web answered first, and the changing state.
public sealed class ConsoleSeamTests : IDisposable
{
    private static readonly Guid s_machineId = Guid.Parse("0193a4b2-0000-7000-8000-000000000001");

    private static readonly AgentSequenceChoice s_install = new(
        Guid.Parse("0193a4b2-0000-7000-8000-00000000e001"),
        "Install Windows",
        "Office PCs",
        ErasesDisk: true,
        NeedsComputerName: true,
        RequiredBytes: 30L * 1024 * 1024 * 1024,
        Suggested: false);

    private static readonly AgentSequenceChoice s_inventory = new(
        Guid.Parse("0193a4b2-0000-7000-8000-00000000e002"),
        "Inventory",
        null,
        ErasesDisk: false,
        NeedsComputerName: false,
        RequiredBytes: 0,
        Suggested: true);

    private static readonly AgentSequenceChoice s_linux = new(
        Guid.Parse("0193a4b2-0000-7000-8000-00000000e003"),
        "Install Linux",
        null,
        ErasesDisk: true,
        NeedsComputerName: false,
        RequiredBytes: 4L * 1024 * 1024 * 1024,
        Suggested: false,
        RawImageName: "custom-image",
        RawImageBootCapability: ImageBootCapability.NotSigned);

    private readonly FakeDeploymentTools _tools = new();
    private readonly TestImage _image = new();

    public void Dispose() => _tools.Dispose();

    private static AgentRegistrationResult Pending() => new(s_machineId, MachineState.Pending, "poll-0", "resume", 10, null);

    private static AgentRegistrationResult Approved() => new(s_machineId, MachineState.Approved, "session-0", "resume", 10, "bob");

    private static AgentNextResult Next(MachineState state, string token, string? signedInBy = null, AgentRun? run = null, bool canPick = false) =>
        new(state, token, "resume", 10, signedInBy, Run: run, CanPickSequence: canPick);

    private static AgentNextResult Choosing(string token) => Next(MachineState.Approved, token, "bob", canPick: true);

    private AgentLoop Loop(ScriptedAgentServer server, ScriptedMachineConsole console, IMachineIdentityReader? identity = null)
    {
        ImmediateTimeProvider time = new();
        AgentLog log = new(time, TextWriter.Null) { MachineConsole = console };

        return TestAgents.Loop(server, TestAgents.Status(console), new(_tools, log, time) { Identity = identity });
    }

    [Fact]
    public async Task SignsInOneFieldAtATimeWithTheErrorOfTheAttemptBefore()
    {
        ScriptedAgentServer server = new ScriptedAgentServer()
            .OnRegister(_ => Pending())
            .OnNext(_ => Next(MachineState.Pending, "poll-1"))
            .OnNext(_ => Next(MachineState.Pending, "poll-2"))
            .OnSignIn(_ => new AgentSignInResult(AgentSignInStatus.Failed))
            .OnNext(_ => Next(MachineState.Pending, "poll-3"))
            .OnSignIn(_ => new AgentSignInResult(AgentSignInStatus.RequiresTwoFactor))
            .OnNext(_ => Next(MachineState.Pending, "poll-4"))
            .OnSignIn(_ => new AgentSignInResult(AgentSignInStatus.Succeeded))
            .OnNext(_ => Next(MachineState.Approved, "session", "bob"));
        ScriptedMachineConsole console = new(Typed("bob"), Typed("wrong"), Typed("right"), Typed("123456"));

        await Loop(server, console).RunAsync(server.Stop.Token);

        Assert.Equal<ConsoleQuestion>(
            [
                new SignInQuestion(SignInField.UserName, null, null),
                new SignInQuestion(SignInField.Password, "bob", null),
                new SignInQuestion(SignInField.Password, "bob", "Wrong user name or password."),
                new SignInQuestion(SignInField.Code, "bob", null),
            ],
            console.Questions);
        Assert.Equal(
            [
                new AgentSignInRequest("bob", "wrong", null),
                new AgentSignInRequest("bob", "right", null),
                new AgentSignInRequest("bob", "right", "123456"),
            ],
            server.SignIns);

        // The warning is still logged, for the machine's log.
        Assert.Contains(console.Lines, line => line is { Level: ConsoleLogLevel.Warning, Text: "Wrong user name or password." });
    }

    [Fact]
    public async Task AnEmptyPasswordGoesBackToTheUserName()
    {
        ScriptedAgentServer server = new ScriptedAgentServer()
            .OnRegister(_ => Pending())
            .OnNext(_ => Next(MachineState.Pending, "poll"))
            .OnNext(_ => Next(MachineState.Pending, "poll"))
            .OnNext(_ => Next(MachineState.Pending, "poll"));
        ScriptedMachineConsole console = new(Typed("bob"), Back);

        await Loop(server, console).RunAsync(server.Stop.Token);

        Assert.Equal<ConsoleQuestion>(
            [
                new SignInQuestion(SignInField.UserName, null, null),
                new SignInQuestion(SignInField.Password, "bob", null),
                new SignInQuestion(SignInField.UserName, null, null),
            ],
            console.Questions);
        Assert.Empty(server.SignIns);
    }

    [Fact]
    public async Task AnApprovalOnTheWebWithdrawsTheSignIn()
    {
        ScriptedMachineConsole console = new();
        int withdrawnWhenApproved = 0;
        ScriptedAgentServer server = new ScriptedAgentServer()
            .OnRegister(_ => Pending())
            .OnNext(_ => Next(MachineState.Pending, "poll"))
            .OnNext(_ => Next(MachineState.Approved, "session"))
            .OnLog(_ => withdrawnWhenApproved = console.Withdrawn);

        await Loop(server, console).RunAsync(server.Stop.Token);

        Assert.Equal(1, withdrawnWhenApproved);
        Assert.Equal<ConsoleQuestion>([new SignInQuestion(SignInField.UserName, null, null)], console.Questions);
        Assert.Empty(server.SignIns);
    }

    [Fact]
    public async Task ShowsWhoSignedInWhileTheWebStillHasToApprove()
    {
        ScriptedMachineConsole console = new(Typed("bob"), Typed("secret"));
        ScriptedAgentServer server = new ScriptedAgentServer()
            .OnRegister(_ => Pending())
            .OnNext(_ => Next(MachineState.Pending, "poll"))
            .OnNext(_ => Next(MachineState.Pending, "poll"))
            .OnSignIn(_ => new AgentSignInResult(AgentSignInStatus.Succeeded))
            .OnNext(_ => Next(MachineState.Pending, "poll", "bob"));

        await Loop(server, console).RunAsync(server.Stop.Token);

        Assert.Contains(console.States, state => state is { Stage: ConsoleStage.WaitingForAuthorization, SignedInBy: "bob" });
        Assert.Equal(2, console.Questions.Count);
    }

    [Fact]
    public async Task ChoosesTheSequenceDiskAndNameByWhatTheyAre()
    {
        _tools.Disks.Add(FakeDeploymentTools.Disk(2, partitions: 3));
        AgentRun picked = TestRuns.Run(TestRuns.InstallWindows, diskNumber: 2);
        ScriptedAgentServer server = new ScriptedAgentServer()
            .OnRegister(_ => Approved())
            .OnNext(_ => Choosing("session-1"))
            .OnSequences(() => [s_install, s_inventory])
            .OnNext(_ => Choosing("session-2"))
            .OnNext(_ => Choosing("session-3"))
            .OnNext(_ => Choosing("session-4"))
            .OnNext(_ => Choosing("session-5"))
            .OnPickSequence(_ => picked);
        ScriptedMachineConsole console = new(Sequence("Install Windows"), Disk(2), Typed("PC_01"), Typed("PC-01"), Typed("ERASE"));

        await Loop(server, console).RunAsync(server.Stop.Token);

        Assert.Equal([new AgentRunRequest(s_install.Id, 2, "PC-01")], server.RunRequests);

        List<ConsoleQuestion> questions = console.Questions;
        Assert.Equal(5, questions.Count);

        // The suggested sequence first, with what each one does.
        SequenceQuestion sequences = Assert.IsType<SequenceQuestion>(questions[0]);
        Assert.Equal(
            [
                new SequenceOption(s_inventory.Id, "Inventory", null, true, false, false, 0, false, false),
                new SequenceOption(s_install.Id, "Install Windows", "Office PCs", false, true, true, s_install.RequiredBytes, false, false),
            ],
            sequences.Sequences);

        DiskQuestion disks = Assert.IsType<DiskQuestion>(questions[1]);
        Assert.Equal("Install Windows", disks.SequenceName);
        Assert.Equal([_tools.Disks[0].ToConsoleDisk(), new ConsoleDisk(2, "Test disk 2", 256L * 1024 * 1024 * 1024, "Nvme", 3)], disks.Disks);

        Assert.Equal(new ComputerNameQuestion("Install Windows", 15, null), questions[2]);
        Assert.Equal(new ComputerNameQuestion("Install Windows", 15, "A computer name can hold only the letters A to Z, digits and hyphens."), questions[3]);
        Assert.Equal(new EraseQuestion("Install Windows", new ConsoleDisk(2, "Test disk 2", 256L * 1024 * 1024 * 1024, "Nvme", 3), "ERASE"), questions[4]);

        Assert.Contains(console.States, state => state.Stage == ConsoleStage.Choosing && state.Machine.Disks?.Count == 2);
    }

    [Theory]
    [InlineData("erase")]
    [InlineData("")]
    [InlineData(null)]
    public async Task AnythingButEraseGoesBackToTheSequences(string? typed)
    {
        ScriptedAgentServer server = new ScriptedAgentServer()
            .OnRegister(_ => Approved())
            .OnNext(_ => Choosing("session-1"))
            .OnSequences(() => [s_install with { NeedsComputerName = false }])
            .OnNext(_ => Choosing("session-2"))
            .OnNext(_ => Choosing("session-3"));
        ScriptedMachineConsole console = new(
            Sequence("Install Windows"),
            typed is null ? Back : Typed(typed));

        await Loop(server, console).RunAsync(server.Stop.Token);

        Assert.Equal([typeof(SequenceQuestion), typeof(EraseQuestion), typeof(SequenceQuestion)], console.Questions.Select(question => question.GetType()));
        Assert.Contains(console.Lines, line => line.Text == "Nothing was erased.");
        Assert.Empty(server.RunRequests);
    }

    [Fact]
    public async Task BackAtTheDiskGoesBackToTheSequences()
    {
        _tools.Disks.Add(FakeDeploymentTools.Disk(1));
        ScriptedAgentServer server = new ScriptedAgentServer()
            .OnRegister(_ => Approved())
            .OnNext(_ => Choosing("session-1"))
            .OnSequences(() => [s_install])
            .OnNext(_ => Choosing("session-2"))
            .OnNext(_ => Choosing("session-3"));
        ScriptedMachineConsole console = new(Sequence("Install Windows"), Back);

        await Loop(server, console).RunAsync(server.Stop.Token);

        Assert.Equal([typeof(SequenceQuestion), typeof(DiskQuestion), typeof(SequenceQuestion)], console.Questions.Select(question => question.GetType()));
    }

    [Fact]
    public async Task AsksForTheSecureBootOverrideWithTheReason()
    {
        ScriptedAgentServer server = new ScriptedAgentServer()
            .OnRegister(_ => Approved())
            .OnNext(_ => Choosing("session-1"))
            .OnSequences(() => [s_linux])
            .OnNext(_ => Choosing("session-2"))
            .OnNext(_ => Choosing("session-3"))
            .OnPickSequence(_ => throw new AgentRequestException("409", "This machine cannot pick a sequence now.", HttpStatusCode.Conflict));
        ScriptedMachineConsole console = new(Sequence("Install Linux"), Typed("ERASE"), Typed("ANYWAY"));

        await Loop(server, console, new DryRunMachineIdentityReader(1, secureBootEnabled: true)).RunAsync(server.Stop.Token);

        SequenceOption option = Assert.Single(Assert.IsType<SequenceQuestion>(console.Questions[0]).Sequences);
        Assert.True(option.NotSignedForSecureBoot);
        Assert.Equal(new SecureBootQuestion("Install Linux", "custom-image", SecureBootProblem.NotSigned, null, "ANYWAY"), console.Questions[2]);
        Assert.Equal([new AgentRunRequest(s_linux.Id, 0, null, AllowSecureBootMismatch: true)], server.RunRequests);
        Assert.Contains(console.States, state => state.Machine is { SecureBootEnabled: true, TrustedUefiCas: MicrosoftUefiCas.Ca2011 | MicrosoftUefiCas.Ca2023 });
    }

    [Fact]
    public async Task AWebAssignmentWithdrawsTheChoice()
    {
        ScriptedMachineConsole console = new();
        int withdrawnWhenAssigned = -1;
        ScriptedAgentServer server = _image.Serve(new ScriptedAgentServer())
            .OnRegister(_ => Approved())
            .OnNext(_ => Choosing("session-1"))
            .OnSequences(() => [s_install])
            .OnNext(_ => Next(MachineState.Approved, "session-2", "bob", TestRuns.Run(TestRuns.InstallWindows, _image)));
        server.AnswerRunReports = (_, token) =>
        {
            if (withdrawnWhenAssigned < 0)
            {
                withdrawnWhenAssigned = console.Withdrawn;
            }

            return new AgentRunReportResult(token, "resume", null);
        };

        int exitCode = await Loop(server, console).RunAsync(server.Stop.Token);

        Assert.Equal(AgentExitCodes.Deployed, exitCode);
        Assert.Equal(1, withdrawnWhenAssigned);
        Assert.Empty(server.RunRequests);
    }

    [Fact]
    public async Task ShowsTheRunStepByStepAndTheRestartIntoWindows()
    {
        ScriptedMachineConsole console = new();
        ScriptedAgentServer server = _image.Serve(new ScriptedAgentServer())
            .OnRegister(_ => Approved())
            .OnNext(_ => Next(MachineState.Approved, "session-1", "bob", TestRuns.Run(TestRuns.InstallWindows, _image)));

        int exitCode = await Loop(server, console).RunAsync(server.Stop.Token);

        Assert.Equal(AgentExitCodes.Deployed, exitCode);
        List<ConsoleState> states = console.States;

        ConsoleRun begun = states.First(state => state.Stage == ConsoleStage.Running).Run!;
        Assert.Equal((TestRuns.RunId, "Install Windows", ConsoleActivity.Preparing), (begun.Id, begun.SequenceName, begun.Activity));
        Assert.Equal(
            [
                new ConsoleStep(TestRuns.Partition.Id, "Partition", "partition", ConsolePhase.WindowsPE, ConsoleStepState.Pending, null),
                new ConsoleStep(TestRuns.Apply.Id, "Apply image", "applyImage", ConsolePhase.WindowsPE, ConsoleStepState.Pending, null),
                new ConsoleStep(TestRuns.Unattend.Id, "Answer file", "writeUnattend", ConsolePhase.WindowsPE, ConsoleStepState.Pending, null),
            ],
            begun.Steps);

        // Each step is shown running, then done.
        Assert.Contains(states, state => state.Run is { CurrentStepId: { } step, Activity: ConsoleActivity.Step } && step == TestRuns.Apply.Id);
        Assert.Contains(states, state => state.Run?.Steps.All(step => step.State == ConsoleStepState.Done) == true);

        Assert.Equal(ConsoleStage.Finished, states.Last(state => state.Stage != ConsoleStage.Restarting).Stage);
        Assert.Equal(new ConsoleRestart(RestartReason.RunDone, RestartTarget.InstalledSystem), states[^1].Restart);
        Assert.Equal(ConsoleStage.Restarting, states[^1].Stage);
    }

    [Fact]
    public async Task ShowsAFailedRunUntilTheNextOneIsChosen()
    {
        _tools.FailAt = "partition";
        ScriptedMachineConsole console = new();
        ScriptedAgentServer server = _image.Serve(new ScriptedAgentServer())
            .OnRegister(_ => Approved())
            .OnNext(_ => Next(MachineState.Approved, "session-1", "bob", TestRuns.Run(TestRuns.InstallWindows, _image)))
            .OnRunReport(DeploymentState.Failed, _ => new AgentRunReportResult("session-f", "resume-f", null))
            .OnNext(_ => Next(MachineState.Failed, "session-2", "bob", canPick: true))
            .OnSequences(() => [s_install]);

        await Loop(server, console).RunAsync(server.Stop.Token);

        ConsoleState failed = console.States.First(state => state.Stage == ConsoleStage.Failed);
        Assert.Equal(ConsoleRemedy.RunAgain, failed.Problem?.Remedy);
        Assert.Contains(failed.Run!.Steps, step => step is { Name: "Partition", State: ConsoleStepState.Failed });

        // The failure stays in sight while the sequences are offered again.
        ConsoleState choosing = console.States.Last(state => state.Stage == ConsoleStage.Choosing);
        Assert.Equal(failed.Problem, choosing.Problem);
        Assert.IsType<SequenceQuestion>(Assert.Single(console.Questions));
    }

    [Fact]
    public async Task ShowsWhyTheServerCannotBeReachedUntilItCan()
    {
        ScriptedMachineConsole console = new() { CanAsk = false };
        ScriptedAgentServer server = new ScriptedAgentServer()
            .OnRegister(_ => throw new HttpRequestException(HttpRequestError.NameResolutionError, "the name ddt.example cannot be found in DNS"))
            .OnRegister(_ => Pending())
            .OnNext(_ => Next(MachineState.Pending, "poll"));

        await Loop(server, console).RunAsync(server.Stop.Token);

        ConsoleState unreachable = console.States.First(state => state.Server.Problem is not null);
        Assert.Equal(ConsoleStage.Connecting, unreachable.Stage);
        Assert.Equal(new ConsoleServer("https://ddt.example:8443/", "the name ddt.example cannot be found in DNS", ConnectionStage.NameLookup, 1), unreachable.Server);

        ConsoleState registered = console.States.First(state => state.Stage == ConsoleStage.WaitingForAuthorization);
        Assert.Equal(new ConsoleServer("https://ddt.example:8443/"), registered.Server);
        Assert.Equal(s_machineId, registered.MachineId);
    }

    [Fact]
    public async Task ShowsTheMachineAsTheAgentReadIt()
    {
        ScriptedMachineConsole console = new() { CanAsk = false };
        ScriptedAgentServer server = new ScriptedAgentServer().OnRegister(_ => Pending());

        await Loop(server, console).RunAsync(server.Stop.Token);

        MachineIdentity identity = new DryRunMachineIdentityReader(1).Read();
        ConsoleMachine machine = console.States[^1].Machine;
        Assert.Equal(
            ("DDT", "Dry run", "DRYRUN-1", identity.SmbiosUuid, "German (Germany)"),
            (machine.Manufacturer, machine.Model, machine.SerialNumber, machine.SmbiosUuid, machine.KeyboardLayout));
        Assert.Equal(identity.MacAddresses, machine.MacAddresses);
        Assert.Equal([_tools.Disks[0].ToConsoleDisk()], machine.Disks);
    }

    [Fact]
    public async Task ARejectedMachineStopsWithWhatCanBeDone()
    {
        ScriptedMachineConsole console = new() { CanAsk = false };
        ScriptedAgentServer server = new ScriptedAgentServer().OnRegister(_ => Pending() with { State = MachineState.Rejected, Token = null });

        Assert.Equal(AgentExitCodes.Rejected, await Loop(server, console).RunAsync(server.Stop.Token));

        ConsoleState last = console.States[^1];
        Assert.Equal(ConsoleStage.Stopped, last.Stage);
        Assert.Equal(ConsoleRemedy.Restart, last.Problem?.Remedy);
        Assert.StartsWith("An administrator rejected this machine.", last.Problem?.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AStopIsTheLastThingShown()
    {
        ScriptedMachineConsole console = new() { CanAsk = false };
        ScriptedAgentServer server = new ScriptedAgentServer().OnRegister(_ => Pending()).OnNext(_ => Next(MachineState.Pending, "poll"));

        Assert.Equal(AgentExitCodes.Stopped, await Loop(server, console).RunAsync(server.Stop.Token));

        Assert.Equal(new ConsoleProblem("The agent was stopped.", ConsoleRemedy.Restart), console.States[^1].Problem);
    }

    // The console shows a step's kind, so every kind has a name, the same as in a sequence's JSON.
    [Fact]
    public void NamesEveryKindOfStepAsItsJsonDoes()
    {
        foreach (JsonDerivedTypeAttribute kind in typeof(SequenceStep).GetCustomAttributes(typeof(JsonDerivedTypeAttribute), inherit: false))
        {
            SequenceStep step = (SequenceStep)RuntimeHelpers.GetUninitializedObject(kind.DerivedType);

            Assert.Equal(kind.TypeDiscriminator, ConsoleValues.KindOf(step));
        }
    }

    // The console says what the agent does between steps, so every activity has a console activity of the same name.
    [Fact]
    public void GivesEveryActivityOfARunItsConsoleActivity()
    {
        foreach (RunActivity activity in Enum.GetValues<RunActivity>())
        {
            Assert.Equal(activity.ToString(), ConsoleValues.ToConsole(activity).ToString());
        }
    }
}
