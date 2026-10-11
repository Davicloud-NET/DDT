// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using DDT.Contracts.Accounts;
using DDT.Contracts.Agents;
using DDT.Contracts.Deployments;
using DDT.Contracts.Sequences;
using DDT.Server.Images;
using Xunit;
using static DDT.Server.Tests.AccountRequests;
using static DDT.Server.Tests.TestReports;

namespace DDT.Server.Tests;

// The installed Linux connects shares, which the Linux boot environment cannot. No script there runs as an account,
// and no machine joins a domain from it. The configured domain is corp.example.
public sealed class InstalledLinuxAccountTests(DomainDeploymentApplication application) : IClassFixture<DomainDeploymentApplication>
{
    private static async Task<string?> RefusedAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        return await TestDatabase.TitleAsync(response);
    }

    // Registers an agent of the environment with the run token, as after the hand-over, and returns its token.
    private static async Task<string> TokenAsync(DeployingMachine machine, AgentEnvironment environment) =>
        (await RegisteredMachine.ReadAsync<AgentRegistrationResult>(await machine.Agent.RegisterAsync(
            machine.Registration with { RunToken = machine.RunToken, Environment = environment }))).Token!;

    [Fact]
    public async Task AStepInTheInstalledLinuxGetsItsShareAndTheLinuxBootEnvironmentDoesNot()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        AccountView account = await administrator.CreatedAccountAsync(Request() with { Name = $"Tools {Guid.NewGuid():N}" });
        Image image = await application.SeedRawImageAsync(RandomNumberGenerator.GetBytes(4096), name: $"noble {Guid.NewGuid():N}");
        RunScriptStep script = SequenceRequests.InLinux() with
        {
            Shares = [new ShareConnection(@"\\files.corp.example\tools", new AccountReference(account.Id, null))],
        };
        SequenceView sequence = await administrator.CreatedSequenceAsync(SequenceRequests.LinuxWithInstalledSteps(image.Id, script));
        using DeployingMachine machine = await DeployingMachine.RegisterAsync(application, DeployingMachine.LinuxBoot());
        (await administrator.PostAsync($"/api/machines/{machine.Id}/approve")).EnsureSuccessStatusCode();
        await machine.NextAsync();

        Assert.Empty(sequence.Problems);
        await administrator.AssignedAsync(machine.Id, sequence.Id, "LINUX-40");
        AgentRun run = Assert.IsType<AgentRun>((await machine.NextAsync()).Run);
        AgentRunReport reached = Report(
            DeploymentState.Running,
            [Step(run.Sequence.Steps[0], StepState.Done), Step(run.Sequence.Steps[1], StepState.Done), Step(script, StepState.Running)]) with
        {
            Phase = SequencePhase.Linux,
        };
        await machine.ReportOkAsync(run.Id, reached);

        Assert.Equal(
            "The Linux boot environment connects no shares, so it gets no account for one.",
            await RefusedAsync(await machine.Agent.RunAccountsAsync(machine.Id, machine.Token, run.Id, script.Id)));

        string installed = await TokenAsync(machine, AgentEnvironment.Linux);
        HttpResponseMessage answered = await machine.Agent.RunAccountsAsync(machine.Id, installed, run.Id, script.Id);

        Assert.Equal(HttpStatusCode.OK, answered.StatusCode);
        AgentStepAccounts accounts = (await answered.Content.ReadFromJsonAsync<AgentStepAccounts>(TestJson.Options, TestContext.Current.CancellationToken))!;
        AgentShareConnection share = Assert.Single(accounts.Shares);
        Assert.Null(accounts.RunAs);
        Assert.Equal((@"\\files.corp.example\tools", @"CORP\svc-ddt", Password), (share.Path, share.UserName, share.Password));
    }

    // The validator lets neither into a Linux sequence, so this is the agent of an installed Linux on a Windows run.
    [Fact]
    public async Task TheInstalledLinuxGetsNoAccountToRunAScriptAsAndNoneToJoinADomain()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        AccountView account = await administrator.CreatedAccountAsync(Request() with { UserName = "svc-tools@corp.example", Hosts = [], RunAs = true });
        Guid imageId = (await application.SeedImageAsync(RandomNumberGenerator.GetBytes(4096))).Id;
        RunScriptStep runAs = new()
        {
            Id = Guid.NewGuid(),
            Name = "Install as the tools account",
            Phase = SequencePhase.Windows,
            Script = "echo installed",
            RunAs = new AccountReference(account.Id, null),
        };
        JoinDomainStep join = new() { Id = Guid.NewGuid(), Name = "Join" };
        SequenceView sequence = await administrator.CreatedSequenceAsync(
            SequenceRequests.Definition([.. SequenceRequests.Minimal(imageId).Steps, runAs, join]));
        using DeployingMachine machine = await DeployingMachine.ApprovedAsync(application, administrator);
        await administrator.AssignedAsync(machine.Id, sequence.Id, "PC-0040");
        AgentRun run = Assert.IsType<AgentRun>((await machine.NextAsync()).Run);
        AgentRunReport reached = Report(
            DeploymentState.Running,
            [.. run.Sequence.Steps.Take(2).Select(step => Step(step, StepState.Done)), Step(runAs, StepState.Running)]) with
        {
            Phase = SequencePhase.Windows,
        };
        await machine.ReportOkAsync(run.Id, reached);

        string installed = await TokenAsync(machine, AgentEnvironment.Linux);

        Assert.Equal(
            "A script runs as an account in Windows, and this agent registered from the installed Linux.",
            await RefusedAsync(await machine.Agent.RunAccountsAsync(machine.Id, installed, run.Id, runAs.Id)));
        Assert.Equal(
            "A machine joins its domain in Windows, and this agent registered from the installed Linux.",
            await RefusedAsync(await machine.Agent.RunCredentialsAsync(machine.Id, installed, run.Id, join.Id)));
    }
}
