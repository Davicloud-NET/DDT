// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Net;
using System.Text.Json;
using DDT.Contracts;
using DDT.Contracts.Applications;
using DDT.Contracts.Deployments;
using DDT.Contracts.Machines;
using DDT.Contracts.Sequences;
using Xunit;

namespace DDT.E2E;

// Applications from the library, installed in the installed Windows of a dry run.
[Trait("Category", "E2E")]
[Collection(DryRunCollection.Name)]
public sealed class ApplicationDryRunTests(DryRunLab lab)
{
    // The agent's exit code for a run that finished.
    private const int Deployed = 4;

    private static readonly TimeSpan s_runTimeout = TimeSpan.FromMinutes(3);

    // A dry run takes every installer's exit code as 0, so the first application asks for a restart with 0. The step
    // names only the second, which brings the first as its dependency.
    [Fact(Timeout = 600_000)]
    public async Task AnApplicationThatAsksForARestartRestartsTheMachineOnceInsideTheStep()
    {
        lab.SkipWhenUnavailable();
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;

        ApplicationView first = await CreateApplicationAsync(Request("E2E restart") with { SuccessExitCodes = [1], RestartExitCodes = [0] }, cancellationToken);
        ApplicationView second = await CreateApplicationAsync(
            Request("E2E after the restart") with { DependsOn = [first.Id], Detection = new FileDetection(@"%ProgramFiles%\E2E\app.exe", "1.0") },
            cancellationToken);
        InstallApplicationsStep install = new()
        {
            Id = Guid.CreateVersion7(),
            Name = "Install applications",
            Applications = [new ApplicationReference(second.Id, ApplicationReferenceKind.Application)],
        };
        RunScriptStep after = DryRunTests.Script("After the applications", SequencePhase.Windows, "echo after");
        SequenceView sequence = await lab.CreateSequenceAsync(
            "Applications",
            [DryRunTests.Partition(), new ApplyImageStep { Id = Guid.CreateVersion7(), Name = "Apply the image", ImageId = lab.Image.Id }, install, after],
            cancellationToken);

        await using AgentProcess agent = lab.StartAgent();
        MachineSummary machine = await lab.WaitForMachineAsync(agent, cancellationToken);
        await lab.ApproveAsync(machine.Id, null, cancellationToken);
        DeploymentSummary assigned = Assert.IsType<DeploymentSummary>((await lab.AssignAsync(machine.Id, new AssignSequenceRequest(sequence.Id, null), cancellationToken)).Deployment);

        Assert.Equal(Deployed, await agent.WaitForExitAsync(s_runTimeout, cancellationToken));
        DeploymentView run = await lab.RunAsync(assigned.Id, cancellationToken);
        Assert.Equal((DeploymentState.Done, null), (run.Summary.State, run.Summary.Error));
        DeploymentStepView installed = Assert.Single(run.Steps, step => step.StepId == install.Id);
        Assert.Equal((StepState.Done, SequencePhase.Windows, 1, (string?)null), (installed.State, installed.Phase, installed.Restarts, installed.Error));
        Assert.Equal(StepState.Done, Assert.Single(run.Steps, step => step.StepId == after.Id).State);

        Assert.Equal(1, agent.Output.Count($"Installing {first.Name}."));
        Assert.Equal(1, agent.Output.Count($"Installed {first.Name}, which asks for a restart with exit code 0."));
        Assert.Equal(1, agent.Output.Count("The machine restarts, then the step goes on with the next application."));
        Assert.Equal(1, agent.Output.Count($"Step {install.Name} restarts the machine after"));
        Assert.Equal(1, agent.Output.Count(@"Dry run: %ProgramFiles%\E2E\app.exe at version 1.0 or later is not looked for, and taken as missing."));
        Assert.Equal(1, agent.Output.Count($"Installing {second.Name}."));
        Assert.Equal(1, agent.Output.Count("Applications: 2 installed."));
        Assert.Equal(1, agent.Output.Lines.Count(line => line.Contains("taken as exit code 0", StringComparison.Ordinal) && line.Contains($"{first.Id:D}.cmd", StringComparison.Ordinal)));
        Assert.Equal(1, agent.Output.Lines.Count(line => line.Contains("taken as exit code 0", StringComparison.Ordinal) && line.Contains($"{second.Id:D}.cmd", StringComparison.Ordinal)));
        Assert.Equal(1, agent.Output.Count($"Step {install.Name} finished after"));

        lab.AssertClean([agent], [], ("The run's detail", JsonSerializer.Serialize(run, DdtJsonContext.Default.DeploymentView)));
    }

    private static SaveApplicationRequest Request(string name) => new(
        0,
        $"{name} {Guid.NewGuid():N}",
        null,
        null,
        null,
        ApplicationKind.Command,
        null,
        "setup.exe /S",
        [0],
        [],
        null,
        [],
        null,
        null,
        5,
        false);

    private Task<ApplicationView> CreateApplicationAsync(SaveApplicationRequest request, CancellationToken cancellationToken) =>
        lab.Api.SendAsync(
            new JsonRequest<SaveApplicationRequest>(HttpMethod.Post, "api/applications", request, DdtJsonContext.Default.SaveApplicationRequest),
            DdtJsonContext.Default.ApplicationView,
            HttpStatusCode.Created,
            cancellationToken);
}
