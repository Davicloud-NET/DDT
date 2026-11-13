// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Net;
using System.Security.Cryptography;
using DDT.Contracts.Agents;
using DDT.Contracts.Applications;
using DDT.Contracts.Rules;
using DDT.Contracts.Sequences;
using DDT.Contracts.Values;
using DDT.Server.Packages;
using Xunit;
using static DDT.Server.Tests.ApplicationRequests;
using static DDT.Server.Tests.Applications.ApplicationRuns;

namespace DDT.Server.Tests.Applications;

// The agent in Windows reads the applications of the step it runs, and fetches their files by hash.
public sealed class RunApplicationRouteTests(DdtApplication application) : IClassFixture<DdtApplication>
{
    private static async Task<string?> RefusedAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        return await TestDatabase.TitleAsync(response);
    }

    [Fact]
    public async Task TheStepGetsItsApplicationsOnlyWhileItRunsInWindows()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        ApplicationView listed = await administrator.CreatedApplicationAsync(
            Command(Unique("Listed"), (await administrator.UploadedApplicationAsync(RandomNumberGenerator.GetBytes(256), "listed.exe")).Files.PackageId) with
            {
                Detection = new ProductCodeDetection("{6F7B9A10-2C4D-4E5F-8A9B-0C1D2E3F4A5B}"),
            });
        ApplicationView added = await administrator.WithFilesAsync("Added");
        InstallApplicationsStep install = Install("Apps", Of(listed));
        SequenceView sequence = await application.SequenceAsync(null, install);
        (DeployingMachine machine, AgentRun run) = await application.AssignedAsync(
            sequence,
            before: assigned => administrator.CreatedRuleAsync(RuleRequests.Rule(
                $"Apps for {assigned.Registration.PrimaryMac}",
                new TestCondition(MachineVariableNames.MacAddress, ConditionOperator.Equals, assigned.Registration.PrimaryMac),
                values: [new NamedValue("Apps", added.Name)])));
        using DeployingMachine held = machine;

        Assert.Equal(
            "This machine has no such run that is running. Report the run as running first.",
            await RefusedAsync(await machine.Agent.RunApplicationsAsync(machine.Id, machine.Token, run.Id, install.Id)));

        await machine.ReportOkAsync(run.Id, Reached(run, 2, SequencePhase.Windows));
        Assert.Equal(
            "Applications install in Windows, and this agent registered from Windows PE.",
            await RefusedAsync(await machine.Agent.RunApplicationsAsync(machine.Id, machine.Token, run.Id, install.Id)));

        string service = await ServiceTokenAsync(machine);
        AgentStepApplications read = await RegisteredMachine.ReadAsync<AgentStepApplications>(
            await machine.Agent.RunApplicationsAsync(machine.Id, service, run.Id, install.Id));

        Assert.Equal([listed.Id, added.Id], read.Applications.Select(planned => planned.Id));
        Assert.Equal(new ProductCodeDetection("{6F7B9A10-2C4D-4E5F-8A9B-0C1D2E3F4A5B}"), read.Applications[0].Detection);

        // The files added at the start come through the run's files route, like those frozen with it.
        Package files = await application.FilesOfAsync(added);
        Assert.Equal(new AgentApplicationFiles(files.Sha256, files.SizeBytes, files.ExpandedBytes, files.Name), read.Applications[1].Files);
        HttpResponseMessage download = await machine.Agent.RunFileAsync(machine.Id, service, run.Id, files.Sha256);
        Assert.Equal(HttpStatusCode.OK, download.StatusCode);
        Assert.Equal(files.SizeBytes, (await download.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken)).LongLength);

        // Another machine's token gets nothing of this machine's run.
        using DeployingMachine other = await DeployingMachine.ApprovedAsync(application, administrator);
        Assert.Equal(HttpStatusCode.Forbidden, (await other.Agent.RunApplicationsAsync(machine.Id, other.Token, run.Id, install.Id)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await other.Agent.RunApplicationsAsync(other.Id, other.Token, run.Id, install.Id)).StatusCode);
    }

    [Fact]
    public async Task AStepThatIsDoneOrInstallsNothingGetsNoApplications()
    {
        ApplicationView app = await (await application.AdministratorAsync()).CreatedApplicationAsync(Command(Unique("Done")));
        InstallApplicationsStep install = Install(null, Of(app));
        RunScriptStep script = new()
        {
            Id = Guid.NewGuid(),
            Name = "After",
            Phase = SequencePhase.Windows,
            Interpreter = ScriptInterpreter.Cmd,
            Script = "echo done",
        };
        (DeployingMachine machine, AgentRun run) = await application.AssignedAsync(await application.SequenceAsync(null, install, script));
        using DeployingMachine held = machine;
        await machine.ReportOkAsync(run.Id, Reached(run, 2, SequencePhase.Windows));
        string service = await ServiceTokenAsync(machine);

        (await machine.Agent.RunReportAsync(machine.Id, service, run.Id, Reached(run, 3, SequencePhase.Windows))).EnsureSuccessStatusCode();

        Assert.Equal(
            "Only a step that is running gets what it needs. Report the step as running first.",
            await RefusedAsync(await machine.Agent.RunApplicationsAsync(machine.Id, service, run.Id, install.Id)));
        Assert.Equal(
            "That step installs no applications.",
            await RefusedAsync(await machine.Agent.RunApplicationsAsync(machine.Id, service, run.Id, script.Id)));
    }

    [Fact]
    public async Task TheLinuxBootEnvironmentGetsNoApplications()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        ApplicationView app = await administrator.CreatedApplicationAsync(Command(Unique("Anything")));
        InstallApplicationsStep install = Install(null, Of(app));
        (DeployingMachine machine, AgentRun run) = await application.AssignedAsync(await application.SequenceAsync(null, install));
        using DeployingMachine held = machine;

        await machine.ReportOkAsync(run.Id, Reached(run, 2, SequencePhase.Windows));
        string linux = await ServiceTokenAsync(machine, AgentEnvironment.LinuxBoot);

        Assert.Equal(
            "Applications install in Windows, and this agent registered from the Linux boot environment.",
            await RefusedAsync(await machine.Agent.RunApplicationsAsync(machine.Id, linux, run.Id, install.Id)));
    }

    [Fact]
    public async Task AStepWithNothingToInstallGetsAnEmptyList()
    {
        InstallApplicationsStep install = Install("Nothing");
        (DeployingMachine machine, AgentRun run) = await application.AssignedAsync(await application.SequenceAsync(null, install));
        using DeployingMachine held = machine;

        await machine.ReportOkAsync(run.Id, Reached(run, 2, SequencePhase.Windows));
        AgentStepApplications read = await RegisteredMachine.ReadAsync<AgentStepApplications>(
            await machine.Agent.RunApplicationsAsync(machine.Id, await ServiceTokenAsync(machine), run.Id, install.Id));

        Assert.Empty(read.Applications);
    }
}
