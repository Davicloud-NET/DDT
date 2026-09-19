using DDT.Agent.Deployment;
using DDT.Contracts.Agents;
using DDT.Contracts.Deployments;
using DDT.Contracts.Machines;
using Xunit;

namespace DDT.Agent.Tests;

public sealed class AgentDeploymentLoopTests : IDisposable
{
    private static readonly Guid s_machineId = Guid.Parse("0193a4b2-0000-7000-8000-000000000001");
    private static readonly AgentImageChoice s_choice = new(Guid.Parse("0193a4b2-0000-7000-8000-00000000a001"), "Windows 11 Pro", "Professional", "en-US", 3000, 10_000);

    private readonly FakeDeploymentTools _tools = new();
    private readonly TestImage _image = new();

    public void Dispose() => _tools.Dispose();

    private static AgentRegistrationResult Registered(MachineState state = MachineState.Approved) =>
        new(s_machineId, state, "session-0", "resume-0", 10, "bob");

    private static AgentNextResult Next(
        MachineState state,
        string token,
        AgentDeployment? deployment = null,
        bool canPick = false,
        bool domainConfigured = false) =>
        new(state, token, "resume", 10, "bob", deployment, canPick, domainConfigured);

    [Fact]
    public async Task RunsAnAssignedDeploymentAndEndsWithTheDeployedExitCode()
    {
        ScriptedAgentServer server = _image.Serve(new ScriptedAgentServer())
            .OnRegister(_ => Registered())
            .OnNext(_ => Next(MachineState.Approved, "session-1", _image.Deployment()));

        int exitCode = await CreateLoop(server, new ScriptedSignInPrompt { IsAvailable = false }).RunAsync(server.Stop.Token);

        Assert.Equal(AgentExitCodes.Deployed, exitCode);
        Assert.Equal(["register", "next session-0", "head session-1", "report Running session-1"], server.Calls.Take(4));
        Assert.Equal("reboot", _tools.Calls[^1]);
    }

    [Fact]
    public async Task RegistrationSendsTheEligibleDisks()
    {
        _tools.Disks.Add(FakeDeploymentTools.Disk(2, partitions: 3));
        ScriptedAgentServer server = new ScriptedAgentServer().OnRegister(_ => Registered(MachineState.Rejected) with { Token = null });

        await CreateLoop(server, new ScriptedSignInPrompt { IsAvailable = false }).RunAsync(server.Stop.Token);

        AgentRegistration registration = Assert.Single(server.Registrations);
        Assert.Equal([_tools.Disks[0].ToAgentDisk(), new AgentDisk(2, "Test disk 2", _tools.Disks[1].SizeBytes, "Nvme", 3)], registration.Disks);
    }

    [Fact]
    public async Task KeepsPollingAfterAFailedRunAndOffersThePicker()
    {
        _tools.FailAt = "partition";
        ScriptedSignInPrompt prompt = new();
        ScriptedAgentServer server = _image.Serve(new ScriptedAgentServer())
            .OnRegister(_ => Registered())
            .OnNext(_ => Next(MachineState.Approved, "session-1", _image.Deployment()))
            .OnReport(DeploymentState.Failed, _ => new AgentDeploymentReportResult("session-f", "resume-f"))
            .OnNext(_ => Next(MachineState.Failed, "session-2", canPick: true))
            .OnImages(() => [s_choice]);

        int exitCode = await CreateLoop(server, prompt).RunAsync(server.Stop.Token);

        Assert.Equal(AgentExitCodes.Stopped, exitCode);
        Assert.Contains("next session-f", server.Calls);
        Assert.Contains("images session-2", server.Calls);
        Assert.Contains("log session-2", server.Calls);
        Assert.Equal(["Image number"], prompt.Labels);
    }

    [Fact]
    public async Task APickStartsTheDeploymentAtTheNextPoll()
    {
        ScriptedSignInPrompt prompt = new("1", "ERASE");
        AgentDeployment picked = _image.Deployment(diskNumber: 0);
        ScriptedAgentServer server = _image.Serve(new ScriptedAgentServer())
            .OnRegister(_ => Registered())
            .OnNext(_ => Next(MachineState.Approved, "session-1", canPick: true))
            .OnImages(() => [s_choice])
            .OnNext(_ => Next(MachineState.Approved, "session-2", canPick: true))
            .OnPick(_ => picked)
            .OnNext(_ => Next(MachineState.Approved, "session-3", picked));

        int exitCode = await CreateLoop(server, prompt).RunAsync(server.Stop.Token);

        Assert.Equal(AgentExitCodes.Deployed, exitCode);
        Assert.Equal([new AgentPickRequest(s_choice.Id, 0, null)], server.Picks);
        Assert.Equal(["Image number", "Type ERASE to continue"], prompt.Labels);
        Assert.Contains("pick session-2", server.Calls);
        Assert.Contains("partition 0", _tools.Calls);
    }

    [Fact]
    public async Task RunsAPickWhoseAnswerWasLost()
    {
        ScriptedSignInPrompt prompt = new("1", "ERASE");
        AgentDeployment picked = _image.Deployment(diskNumber: 0);
        ScriptedAgentServer server = _image.Serve(new ScriptedAgentServer())
            .OnRegister(_ => Registered())
            .OnNext(_ => Next(MachineState.Approved, "session-1", canPick: true))
            .OnImages(() => [s_choice])
            .OnNext(_ => Next(MachineState.Approved, "session-2", canPick: true))
            .OnPick(_ => throw new HttpRequestException("The connection was reset."))
            .OnNext(_ => Next(MachineState.Approved, "session-3", picked));

        int exitCode = await CreateLoop(server, prompt).RunAsync(server.Stop.Token);

        Assert.Equal(AgentExitCodes.Deployed, exitCode);
        Assert.Contains("partition 0", _tools.Calls);
    }

    [Fact]
    public async Task DoesNotRunADiskChoiceMadeBeforeTheAgentStarted()
    {
        ScriptedAgentServer server = _image.Serve(new ScriptedAgentServer())
            .OnRegister(_ => Registered())
            .OnNext(_ => Next(MachineState.Approved, "session-1", _image.Deployment(diskNumber: 0)));

        await CreateLoop(server, new ScriptedSignInPrompt { IsAvailable = false }).RunAsync(server.Stop.Token);

        AgentDeploymentReport report = Assert.Single(server.Reports);
        Assert.Equal(DeploymentState.Failed, report.State);
        Assert.StartsWith("Disk 0 was chosen before the agent started again", report.Error, StringComparison.Ordinal);
        Assert.DoesNotContain(_tools.Calls, call => call.StartsWith("partition", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AsksForAComputerNameWhenTheDomainNeedsOne()
    {
        ScriptedSignInPrompt prompt = new("1", "PC-7", "ERASE");
        ScriptedAgentServer server = new ScriptedAgentServer()
            .OnRegister(_ => Registered())
            .OnNext(_ => Next(MachineState.Approved, "session-1", canPick: true, domainConfigured: true))
            .OnImages(() => [s_choice])
            .OnNext(_ => Next(MachineState.Approved, "session-2", canPick: true, domainConfigured: true))
            .OnNext(_ => Next(MachineState.Approved, "session-3", canPick: true, domainConfigured: true))
            .OnPick(_ => throw new AgentRequestException("409", "This machine cannot pick an image now.", System.Net.HttpStatusCode.Conflict));

        await CreateLoop(server, prompt).RunAsync(server.Stop.Token);

        Assert.Equal([new AgentPickRequest(s_choice.Id, 0, "PC-7")], server.Picks);
        Assert.Equal(["Image number", "Computer name", "Type ERASE to continue"], prompt.Labels);
    }

    [Fact]
    public async Task AWebAssignmentTakesThePickerAway()
    {
        ScriptedSignInPrompt prompt = new();
        int cancelledWhenAssigned = -1;
        ScriptedAgentServer server = _image.Serve(new ScriptedAgentServer())
            .OnRegister(_ => Registered())
            .OnNext(_ => Next(MachineState.Approved, "session-1", canPick: true))
            .OnImages(() => [s_choice])
            .OnNext(_ => Next(MachineState.Approved, "session-2", _image.Deployment()));
        server.AnswerReports = (report, token) =>
        {
            if (cancelledWhenAssigned < 0)
            {
                cancelledWhenAssigned = prompt.Cancelled;
            }

            return new AgentDeploymentReportResult(token, "resume");
        };

        int exitCode = await CreateLoop(server, prompt).RunAsync(server.Stop.Token);

        Assert.Equal(AgentExitCodes.Deployed, exitCode);
        Assert.Equal(1, cancelledWhenAssigned);
        Assert.Empty(server.Picks);
    }

    [Fact]
    public async Task ReportsARunningDeploymentItDoesNotRunAsFailed()
    {
        ScriptedAgentServer server = new ScriptedAgentServer()
            .OnRegister(_ => Registered(MachineState.Deploying))
            .OnNext(_ => Next(MachineState.Deploying, "session-1", _image.Deployment(state: DeploymentState.Running)))
            .OnReport(DeploymentState.Failed, _ => new AgentDeploymentReportResult("session-f", "resume-f"))
            .OnNext(_ => Next(MachineState.Failed, "session-2"));

        await CreateLoop(server, new ScriptedSignInPrompt { IsAvailable = false }).RunAsync(server.Stop.Token);

        Assert.Equal(
            new AgentDeploymentReport(DeploymentState.Failed, DeploymentStep.Partition, 0, "The agent lost contact with the server during the deployment."),
            Assert.Single(server.Reports));
        Assert.Contains("log session-f", server.Calls);
        Assert.DoesNotContain(_tools.Calls, call => call != "list");
    }

    [Fact]
    public async Task ARunThatLostItsTokenIsReportedWhereItEndedAfterRegisteringAgain()
    {
        AgentDeployment deployment = _image.Deployment();
        ScriptedAgentServer server = new ScriptedAgentServer()
            .OnRegister(_ => Registered())
            .OnNext(_ => Next(MachineState.Approved, "session-1", deployment))
            .OnHeadImage(() => _image.Content.Length)
            .OnOpenImage(_image.From)
            .OnUnattend(() => throw new AgentTokenRejectedException())
            .OnRegister(_ => Registered(MachineState.Deploying))
            .OnNext(_ => Next(MachineState.Deploying, "session-2", deployment with { State = DeploymentState.Running }));

        await CreateLoop(server, new ScriptedSignInPrompt { IsAvailable = false }).RunAsync(server.Stop.Token);

        Assert.Equal(2, server.Registrations.Count);
        AgentDeploymentReport report = server.Reports[^1];
        Assert.Equal(DeploymentState.Failed, report.State);
        Assert.Equal(DeploymentStep.Unattend, report.Step);
        Assert.Equal("The agent lost contact with the server during the deployment.", report.Error);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ReportsTheRealFailureWhenTheRunCouldNotReportIt(bool tokenRefused)
    {
        _tools.FailAt = "bcd";
        AgentDeployment deployment = _image.Deployment();
        ScriptedAgentServer server = _image.Serve(new ScriptedAgentServer())
            .OnRegister(_ => Registered())
            .OnNext(_ => Next(MachineState.Approved, "session-1", deployment));

        if (tokenRefused)
        {
            server.OnReport(DeploymentState.Failed, _ => throw new AgentTokenRejectedException())
                .OnRegister(_ => Registered(MachineState.Deploying));
        }
        else
        {
            for (int attempt = 0; attempt <= ServerCallRules.MaxRetries; attempt++)
            {
                server.OnReport(DeploymentState.Failed, _ => throw new HttpRequestException("503", null, System.Net.HttpStatusCode.ServiceUnavailable));
            }
        }

        server.OnNext(_ => Next(MachineState.Deploying, "session-2", deployment with { State = DeploymentState.Running }));

        await CreateLoop(server, new ScriptedSignInPrompt { IsAvailable = false }).RunAsync(server.Stop.Token);

        // Every attempt, the run's and then the loop's, carries the step's own failure.
        AgentDeploymentReport expected = new(DeploymentState.Failed, DeploymentStep.Boot, 0, "The scripted step failed.");
        Assert.Equal(tokenRefused ? 2 : ServerCallRules.MaxRetries + 2, server.Reports.Count(report => report == expected));
        Assert.Equal(expected, server.Reports[^1]);
    }

    private AgentLoop CreateLoop(ScriptedAgentServer server, ScriptedSignInPrompt prompt)
    {
        ImmediateTimeProvider time = new();

        return TestAgents.Loop(server, prompt, _tools, new AgentLog(time, TextWriter.Null), time);
    }
}
