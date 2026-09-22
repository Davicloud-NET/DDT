// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using DDT.Contracts.Agents;
using DDT.Contracts.Deployments;
using DDT.Contracts.Sequences;
using DDT.Server.Deployments;
using DDT.Server.Machines;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;
using static DDT.Server.Tests.TestReports;

namespace DDT.Server.Tests;

// The answer file and the join account are handed out only while the step that needs them runs.
public sealed class RunSecretTests(DomainDeploymentApplication application) : IClassFixture<DomainDeploymentApplication>
{
    private static readonly XNamespace s_unattend = "urn:schemas-microsoft-com:unattend";

    // Partition, apply, the answer file and the join, as the Install Windows template makes it with a domain. A script
    // given runs in between.
    private async Task<(DeployingMachine Machine, AgentRun Run)> AssignedAsync(
        WriteUnattendStep? unattend = null,
        JoinDomainStep? join = null,
        RunScriptStep? script = null)
    {
        SignedInClient administrator = await application.AdministratorAsync();
        DeployingMachine machine = await DeployingMachine.ApprovedAsync(application, administrator);
        Guid imageId = (await application.SeedImageAsync(RandomNumberGenerator.GetBytes(4096))).Id;
        SequenceView sequence = await administrator.CreatedSequenceAsync(SequenceRequests.Definition(
        [
            .. SequenceRequests.Minimal(imageId).Steps,
            unattend ?? new WriteUnattendStep { Id = Guid.NewGuid(), Name = "Answer file", LocalAdministrator = true },
            .. script is null ? Array.Empty<SequenceStep>() : [script],
            join ?? new JoinDomainStep { Id = Guid.NewGuid(), Name = "Join", RebootAfter = true },
        ]));

        await administrator.AssignedAsync(machine.Id, sequence.Id, "PC-0005");

        return (machine, (await machine.NextAsync()).Run!);
    }

    private static AgentRunReport Reached(AgentRun run, int running, SequencePhase phase = SequencePhase.WindowsPE) =>
        Report(
            DeploymentState.Running,
            [.. run.Sequence.Steps.Take(running + 1).Select((step, index) => Step(step, index < running ? StepState.Done : StepState.Running))],
            phase: phase);

    // The service in Windows, as it registers with the run token the agent in Windows PE handed over.
    private static async Task<string> ServiceTokenAsync(DeployingMachine machine) =>
        (await RegisteredMachine.ReadAsync<AgentRegistrationResult>(await machine.Agent.RegisterAsync(
            machine.Registration with { RunToken = machine.RunToken, Environment = AgentEnvironment.Windows }))).Token!;

    private DeploymentOptions Settings => application.Services.GetRequiredService<IOptions<DeploymentOptions>>().Value;

    private Task<List<string?>> SecretReadsAsync(Guid runId)
    {
        string subject = runId.ToString("D");

        return application.QueryAsync(database => database.AuditEvents
            .Where(e => e.SubjectId == subject && e.Action == AuditActions.DeploymentSecretRead)
            .OrderBy(e => e.Id)
            .Select(e => e.Detail)
            .ToListAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task TheAnswerFileGoesOnlyToTheStepThatWritesItWhileItRuns()
    {
        (DeployingMachine machine, AgentRun run) = await AssignedAsync();
        using DeployingMachine _ = machine;
        Guid unattend = run.Sequence.Steps[2].Id;

        HttpResponseMessage early = await machine.Agent.RunUnattendAsync(machine.Id, machine.Token, run.Id, unattend);
        Assert.Equal(HttpStatusCode.Conflict, early.StatusCode);
        Assert.StartsWith("This machine has no such run that is running.", await TestDatabase.TitleAsync(early), StringComparison.Ordinal);

        await machine.ReportOkAsync(run.Id, Reached(run, 1));
        Assert.Equal(HttpStatusCode.Conflict, (await machine.Agent.RunUnattendAsync(machine.Id, machine.Token, run.Id, unattend)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await machine.Agent.RunUnattendAsync(machine.Id, machine.Token, run.Id, run.Sequence.Steps[1].Id)).StatusCode);

        await machine.ReportOkAsync(run.Id, Reached(run, 2));
        HttpResponseMessage response = await machine.Agent.RunUnattendAsync(machine.Id, machine.Token, run.Id, unattend);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/xml", response.Content.Headers.ContentType?.MediaType);
        Assert.True(response.Headers.CacheControl?.NoStore);

        string xml = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        XDocument answer = XDocument.Parse(xml);
        string Value(string name) => answer.Descendants(s_unattend + name).Single().Value;

        Assert.Equal("PC-0005", Value("ComputerName"));
        Assert.Equal("W. Europe Standard Time", Value("TimeZone"));
        Assert.Equal("en-US", Value("UILanguage"));
        Assert.Equal("en-US", Value("SystemLocale"));
        Assert.Equal("0407:00000407", Value("InputLocale"));
        Assert.Equal("Admin", answer.Descendants(s_unattend + "LocalAccount").Single().Element(s_unattend + "Name")?.Value);
        Assert.Equal(
            Convert.ToBase64String(Encoding.Unicode.GetBytes(DomainDeploymentApplication.AdministratorPassword + "Password")),
            answer.Descendants(s_unattend + "LocalAccount").Single().Descendants(s_unattend + "Value").Single().Value);

        // The machine joins its domain later, in Windows: the join account stays out of Panther\unattend.xml.
        Assert.Empty(answer.Descendants(s_unattend + "JoinDomain"));
        Assert.DoesNotContain("ddt-join", xml, StringComparison.Ordinal);
        Assert.DoesNotContain(DomainDeploymentApplication.JoinPassword, xml, StringComparison.Ordinal);

        Assert.Equal([$"The answer file of step Answer file ({unattend:D}) of {run.SequenceName}."], await SecretReadsAsync(run.Id));

        // Another machine's token never reaches this machine's answer file.
        using DeployingMachine other = await DeployingMachine.ApprovedAsync(application, await application.AdministratorAsync());
        Assert.Equal(HttpStatusCode.Forbidden, (await other.Agent.RunUnattendAsync(machine.Id, other.Token, run.Id, unattend)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await other.Agent.RunUnattendAsync(other.Id, other.Token, run.Id, unattend)).StatusCode);

        await machine.ReportOkAsync(run.Id, Reached(run, 3));
        Assert.Equal(HttpStatusCode.Conflict, (await machine.Agent.RunUnattendAsync(machine.Id, machine.Token, run.Id, unattend)).StatusCode);
    }

    [Fact]
    public async Task TheStepsOwnSettingsComeBeforeTheServers()
    {
        WriteUnattendStep step = new()
        {
            Id = Guid.NewGuid(),
            Name = "Answer file",
            TimeZone = "Pacific Standard Time",
            Locale = "de-CH",
            Keyboard = "0807:00000807",
        };
        (DeployingMachine machine, AgentRun run) = await AssignedAsync(unattend: step);
        using DeployingMachine _ = machine;

        await machine.ReportOkAsync(run.Id, Reached(run, 2));
        HttpResponseMessage response = await machine.Agent.RunUnattendAsync(machine.Id, machine.Token, run.Id, step.Id);
        XDocument answer = XDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

        Assert.Equal("Pacific Standard Time", answer.Descendants(s_unattend + "TimeZone").Single().Value);
        Assert.Equal("de-CH", answer.Descendants(s_unattend + "UserLocale").Single().Value);
        Assert.Equal("0807:00000807", answer.Descendants(s_unattend + "InputLocale").Single().Value);
        Assert.Empty(answer.Descendants(s_unattend + "LocalAccount"));
    }

    [Fact]
    public async Task TheJoinAccountGoesOnlyToTheServiceInWindowsWhileTheJoinRuns()
    {
        (DeployingMachine machine, AgentRun run) = await AssignedAsync(join: new JoinDomainStep
        {
            Id = Guid.NewGuid(),
            Name = "Join",
            OrganizationalUnit = "OU=Kiosks,DC=corp,DC=example",
        });
        using DeployingMachine _ = machine;
        Guid join = run.Sequence.Steps[3].Id;

        await machine.ReportOkAsync(run.Id, Reached(run, 3, SequencePhase.Windows));

        HttpResponseMessage fromWindowsPE = await machine.Agent.RunCredentialsAsync(machine.Id, machine.Token, run.Id, join);
        Assert.Equal(HttpStatusCode.Conflict, fromWindowsPE.StatusCode);
        Assert.Equal("A machine joins its domain in Windows, and this agent registered from Windows PE.", await TestDatabase.TitleAsync(fromWindowsPE));

        AgentRegistrationResult service = await RegisteredMachine.ReadAsync<AgentRegistrationResult>(await machine.Agent.RegisterAsync(
            machine.Registration with { ResumeToken = machine.ResumeToken, Environment = AgentEnvironment.Windows }));

        HttpResponseMessage response = await machine.Agent.RunCredentialsAsync(machine.Id, service.Token!, run.Id, join);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);

        AgentJoinDomainCredentials credentials = (await response.Content.ReadFromJsonAsync<AgentJoinDomainCredentials>(TestJson.Options, TestContext.Current.CancellationToken))!;

        Assert.Equal("corp.example", credentials.Domain);
        Assert.Equal("OU=Kiosks,DC=corp,DC=example", credentials.OrganizationalUnit);
        Assert.Equal(@"CORP\ddt-join", credentials.UserName);
        Assert.Equal(DomainDeploymentApplication.JoinPassword, credentials.Password);
        Assert.Equal(
            [$"The domain join credentials of step Join ({join:D}) of {run.SequenceName}, for corp.example."],
            await SecretReadsAsync(run.Id));

        // The answer file step is over, and a step that joins nothing gets no account.
        Assert.Equal(HttpStatusCode.Conflict, (await machine.Agent.RunCredentialsAsync(machine.Id, service.Token!, run.Id, run.Sequence.Steps[2].Id)).StatusCode);
    }

    // Only the join gets the account, not any other step that runs in Windows.
    [Fact]
    public async Task AScriptInWindowsGetsNoJoinAccount()
    {
        RunScriptStep script = (RunScriptStep)SequenceRequests.ScriptOnly().Steps[0] with { Phase = SequencePhase.Windows };
        (DeployingMachine machine, AgentRun run) = await AssignedAsync(script: script);
        using DeployingMachine _ = machine;

        await machine.ReportOkAsync(run.Id, Reached(run, 3, SequencePhase.Windows));
        HttpResponseMessage refused = await machine.Agent.RunCredentialsAsync(machine.Id, await ServiceTokenAsync(machine), run.Id, script.Id);

        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        Assert.Equal("That step joins no domain.", await TestDatabase.TitleAsync(refused));
        Assert.Empty(await SecretReadsAsync(run.Id));
    }

    // The settings can lose a secret with a restart of the server while the run goes on. The step then gets nothing.
    [Fact]
    public async Task ASecretGoneFromTheSettingsIsNotServed()
    {
        (DeployingMachine machine, AgentRun run) = await AssignedAsync();
        using DeployingMachine _ = machine;
        string? administratorPassword = Settings.LocalAdministrator.Password;
        string? joinPassword = Settings.Domain.Password;

        await machine.ReportOkAsync(run.Id, Reached(run, 2));

        try
        {
            Settings.LocalAdministrator.Password = string.Empty;
            HttpResponseMessage noAnswerFile = await machine.Agent.RunUnattendAsync(machine.Id, machine.Token, run.Id, run.Sequence.Steps[2].Id);

            Assert.Equal(HttpStatusCode.Conflict, noAnswerFile.StatusCode);
            Assert.Equal(
                "The step adds the local administrator, but DDT:Deployment:LocalAdministrator has no password any more.",
                await TestDatabase.TitleAsync(noAnswerFile));

            await machine.ReportOkAsync(run.Id, Reached(run, 3, SequencePhase.Windows));
            Settings.Domain.Password = string.Empty;
            HttpResponseMessage noAccount = await machine.Agent.RunCredentialsAsync(machine.Id, await ServiceTokenAsync(machine), run.Id, run.Sequence.Steps[3].Id);

            Assert.Equal(HttpStatusCode.Conflict, noAccount.StatusCode);
            Assert.Equal("DDT:Deployment:Domain no longer names an account to join the domain with.", await TestDatabase.TitleAsync(noAccount));
        }
        finally
        {
            Settings.LocalAdministrator.Password = administratorPassword;
            Settings.Domain.Password = joinPassword;
        }

        Assert.Empty(await SecretReadsAsync(run.Id));
    }

    // The account is bound to the domain the run started with, so a domain configured since gets nothing.
    [Fact]
    public async Task AJoinForAnotherDomainGetsNoAccount()
    {
        (DeployingMachine machine, AgentRun run) = await AssignedAsync();
        using DeployingMachine _ = machine;

        await machine.ReportOkAsync(run.Id, Reached(run, 3, SequencePhase.Windows));
        RunInputs inputs = RunInputs.Read((await application.QueryAsync(database => database.Deployments.SingleAsync(d => d.Id == run.Id, TestContext.Current.CancellationToken))).Inputs!);
        string moved = (inputs with { DomainName = "old.example" }).Write();
        await application.QueryAsync(database => database.Deployments
            .Where(d => d.Id == run.Id)
            .ExecuteUpdateAsync(d => d.SetProperty(x => x.Inputs, moved), TestContext.Current.CancellationToken));

        AgentRegistrationResult service = await RegisteredMachine.ReadAsync<AgentRegistrationResult>(await machine.Agent.RegisterAsync(
            machine.Registration with { ResumeToken = machine.ResumeToken, Environment = AgentEnvironment.Windows }));
        HttpResponseMessage refused = await machine.Agent.RunCredentialsAsync(machine.Id, service.Token!, run.Id, run.Sequence.Steps[3].Id);

        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        Assert.StartsWith("The configured domain changed after the run started", await TestDatabase.TitleAsync(refused), StringComparison.Ordinal);
        Assert.Empty(await SecretReadsAsync(run.Id));
    }
}
