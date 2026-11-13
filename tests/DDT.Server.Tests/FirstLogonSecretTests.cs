// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using DDT.Contracts.Accounts;
using DDT.Contracts.Agents;
using DDT.Contracts.Deployments;
using DDT.Contracts.Sequences;
using DDT.Server.Machines;
using Microsoft.EntityFrameworkCore;
using Xunit;
using static DDT.Server.Tests.AccountRequests;
using static DDT.Server.Tests.TestReports;

namespace DDT.Server.Tests;

// The first-logon commands and the automatic logon go into the answer file without a Windows step, and otherwise to
// the agent in Windows as the run ends there.
public sealed class FirstLogonSecretTests(DomainDeploymentApplication application) : IClassFixture<DomainDeploymentApplication>
{
    private static readonly XNamespace s_unattend = "urn:schemas-microsoft-com:unattend";

    private static WriteUnattendStep Unattend(AutoLogonKind autoLogon, AccountReference? account = null) => new()
    {
        Id = Guid.NewGuid(),
        Name = "Answer file",
        LocalAdministrator = true,
        FirstLogonCommands = ["cmd /c echo one", "cmd /c echo two"],
        AutoLogon = autoLogon,
        AutoLogonAccount = account,
        AutoLogonCount = 2,
    };

    // Partition, apply and the answer file, and a join in Windows when windows is true.
    private async Task<(DeployingMachine Machine, AgentRun Run)> AssignedAsync(SignedInClient administrator, WriteUnattendStep unattend, bool windows)
    {
        DeployingMachine machine = await DeployingMachine.ApprovedAsync(application, administrator);
        Guid imageId = (await application.SeedImageAsync(RandomNumberGenerator.GetBytes(4096))).Id;
        SequenceView sequence = await administrator.CreatedSequenceAsync(SequenceRequests.Definition(
        [
            .. SequenceRequests.Minimal(imageId).Steps,
            unattend,
            .. windows ? [new JoinDomainStep { Id = Guid.NewGuid(), Name = "Join", RebootAfter = true }] : Array.Empty<SequenceStep>(),
        ]));

        await administrator.AssignedAsync(machine.Id, sequence.Id, "PC-0005");

        return (machine, (await machine.NextAsync()).Run!);
    }

    private static AgentRunReport Reached(AgentRun run, int running, SequencePhase phase = SequencePhase.WindowsPE) =>
        Report(
            DeploymentState.Running,
            [.. run.Sequence.Steps.Take(running + 1).Select((step, index) => Step(step, index < running ? StepState.Done : StepState.Running))]) with
        {
            Phase = phase,
        };

    private static async Task<string> ServiceTokenAsync(DeployingMachine machine) =>
        (await RegisteredMachine.ReadAsync<AgentRegistrationResult>(await machine.Agent.RegisterAsync(
            machine.Registration with { RunToken = machine.RunToken, Environment = AgentEnvironment.Windows }))).Token!;

    private Task<List<string?>> SecretReadsAsync(Guid runId)
    {
        string subject = runId.ToString("D");

        return application.QueryAsync(database => database.AuditEvents
            .Where(e => e.SubjectId == subject && e.Action == AuditActions.DeploymentSecretRead)
            .OrderBy(e => e.Id)
            .Select(e => e.Detail)
            .ToListAsync(TestContext.Current.CancellationToken));
    }

    private static string Encoded(string password) => Convert.ToBase64String(Encoding.Unicode.GetBytes(password + "Password"));

    private void AssertNeverLogged(params string[] secrets) =>
        Assert.All(application.Log.Entries, entry => Assert.All(secrets, secret =>
        {
            Assert.DoesNotContain(secret, entry.Message, StringComparison.Ordinal);
            Assert.DoesNotContain(secret, entry.Exception ?? string.Empty, StringComparison.Ordinal);
        }));

    [Fact]
    public async Task WithoutAWindowsStepTheAnswerFileSignsInAsTheLocalAdministratorAndRunsTheCommands()
    {
        (DeployingMachine machine, AgentRun run) = await AssignedAsync(await application.AdministratorAsync(), Unattend(AutoLogonKind.LocalAdministrator), windows: false);
        using DeployingMachine _ = machine;
        Guid unattend = run.Sequence.Steps[2].Id;

        Assert.False(run.SetsUpFirstLogon);

        await machine.ReportOkAsync(run.Id, Reached(run, 2));
        HttpResponseMessage response = await machine.Agent.RunUnattendAsync(machine.Id, machine.Token, run.Id, unattend);
        response.EnsureSuccessStatusCode();
        XDocument answer = XDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        XElement autoLogon = answer.Descendants(s_unattend + "AutoLogon").Single();

        Assert.Equal("Admin", autoLogon.Element(s_unattend + "Username")?.Value);
        Assert.Null(autoLogon.Element(s_unattend + "Domain"));
        Assert.Equal("1", autoLogon.Element(s_unattend + "LogonCount")?.Value);
        Assert.Equal(Encoded(DomainDeploymentApplication.AdministratorPassword), autoLogon.Descendants(s_unattend + "Value").Single().Value);
        Assert.Equal(
            ["cmd.exe /d /s /c \"cmd /c echo one\"", "cmd.exe /d /s /c \"cmd /c echo two\""],
            answer.Descendants(s_unattend + "CommandLine").Select(command => command.Value));
        Assert.Equal(
            [$"The answer file of step Answer file ({unattend:D}) of {run.SequenceName}, with the local administrator Admin to sign in as."],
            await SecretReadsAsync(run.Id));
        AssertNeverLogged(DomainDeploymentApplication.AdministratorPassword, Encoded(DomainDeploymentApplication.AdministratorPassword));
    }

    [Fact]
    public async Task WithAWindowsStepTheAnswerFileLeavesThemToTheAgent()
    {
        (DeployingMachine machine, AgentRun run) = await AssignedAsync(await application.AdministratorAsync(), Unattend(AutoLogonKind.LocalAdministrator), windows: true);
        using DeployingMachine _ = machine;

        Assert.True(run.SetsUpFirstLogon);

        await machine.ReportOkAsync(run.Id, Reached(run, 2));
        string xml = await (await machine.Agent.RunUnattendAsync(machine.Id, machine.Token, run.Id, run.Sequence.Steps[2].Id)).Content
            .ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.Contains("<LocalAccount", xml, StringComparison.Ordinal);
        Assert.DoesNotContain("AutoLogon", xml, StringComparison.Ordinal);
        Assert.DoesNotContain("FirstLogonCommands", xml, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheAgentInWindowsGetsThemOnlyWhileItsRunRunsThere()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        AccountView account = await administrator.CreatedAccountAsync(Request());
        (DeployingMachine machine, AgentRun run) = await AssignedAsync(
            administrator,
            Unattend(AutoLogonKind.Account, new AccountReference(account.Id, null)),
            windows: true);
        using DeployingMachine _ = machine;

        await machine.ReportOkAsync(run.Id, Reached(run, 2));
        HttpResponseMessage fromWindowsPE = await machine.Agent.RunFirstLogonAsync(machine.Id, machine.Token, run.Id);
        Assert.Equal(HttpStatusCode.Conflict, fromWindowsPE.StatusCode);
        Assert.Equal("Only this machine's run gets them, while it runs in Windows.", await TestDatabase.TitleAsync(fromWindowsPE));

        await machine.ReportOkAsync(run.Id, Reached(run, 3, SequencePhase.Windows));
        string service = await ServiceTokenAsync(machine);
        HttpResponseMessage response = await machine.Agent.RunFirstLogonAsync(machine.Id, service, run.Id);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        AgentFirstLogon firstLogon = (await response.Content.ReadFromJsonAsync<AgentFirstLogon>(TestJson.Options, TestContext.Current.CancellationToken))!;

        Assert.Equal(["cmd /c echo one", "cmd /c echo two"], firstLogon.Commands);
        Assert.Equal(new AgentAutoLogon("svc-ddt", "CORP", Password, 2), firstLogon.AutoLogon);
        Assert.DoesNotContain(Password, firstLogon.AutoLogon!.ToString(), StringComparison.Ordinal);
        Assert.Equal(
            [$"The automatic logon of step Answer file ({run.Sequence.Steps[2].Id:D}) of {run.SequenceName}, with the account {account.Name} ({account.Id:D})."],
            await SecretReadsAsync(run.Id));

        // Another machine's token never reaches it.
        using DeployingMachine other = await DeployingMachine.ApprovedAsync(application, administrator);
        Assert.Equal(HttpStatusCode.Forbidden, (await other.Agent.RunFirstLogonAsync(machine.Id, other.Token, run.Id)).StatusCode);

        await machine.ReportOkAsync(run.Id, Report(DeploymentState.Done, [.. run.Sequence.Steps.Select(s => Step(s, StepState.Done))]) with { Phase = SequencePhase.Windows });
        // The run is over, and with it the service's token.
        Assert.Equal(HttpStatusCode.Unauthorized, (await machine.Agent.RunFirstLogonAsync(machine.Id, service, run.Id)).StatusCode);
        AssertNeverLogged(Password);
    }

    [Fact]
    public async Task ARunWhoseAnswerFileHoldsThemGetsNothingInWindows()
    {
        (DeployingMachine machine, AgentRun run) = await AssignedAsync(await application.AdministratorAsync(), Unattend(AutoLogonKind.None), windows: false);
        using DeployingMachine _ = machine;

        await machine.ReportOkAsync(run.Id, Reached(run, 2, SequencePhase.Windows));
        HttpResponseMessage refused = await machine.Agent.RunFirstLogonAsync(machine.Id, await ServiceTokenAsync(machine), run.Id);

        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        Assert.Equal("The run's answer file holds them.", await TestDatabase.TitleAsync(refused));
    }

    [Fact]
    public async Task AnAccountASequenceSignsInAsCannotBeDeleted()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        AccountView account = await administrator.CreatedAccountAsync(Request());
        WriteUnattendStep unattend = Unattend(AutoLogonKind.Account, new AccountReference(account.Id, null));
        Guid imageId = (await application.SeedImageAsync(RandomNumberGenerator.GetBytes(4096))).Id;
        await administrator.CreatedSequenceAsync(SequenceRequests.Definition([.. SequenceRequests.Minimal(imageId).Steps, unattend]));

        AccountUse use = Assert.Single((await administrator.AccountAsync(account.Id)).UsedBy);
        Assert.Equal([new AccountStepUse(unattend.Id, unattend.Name, "autoLogonAccount")], use.Steps!);

        HttpResponseMessage refused = await administrator.DeleteAccountAsync(account.Id, await administrator.TokenAsync());

        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        Assert.Equal("stepAccount.inUse", await CodeAsync(refused));
    }
}
