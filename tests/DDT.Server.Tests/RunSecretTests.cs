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
using DDT.Contracts.Rules;
using DDT.Contracts.Sequences;
using DDT.Contracts.Values;
using DDT.Server.Deployments;
using DDT.Server.Machines;
using DDT.Server.Settings;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static DDT.Server.Tests.TestReports;

namespace DDT.Server.Tests;

// The answer file and the join account are handed out only while the step that needs them runs.
public sealed class RunSecretTests(DomainDeploymentApplication application) : IClassFixture<DomainDeploymentApplication>
{
    private static readonly XNamespace s_unattend = "urn:schemas-microsoft-com:unattend";

    // Partition, apply, the answer file and the join, like the Install Windows template makes it with a domain.
    // A given script runs in between.
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
            [.. run.Sequence.Steps.Take(running + 1).Select((step, index) => Step(step, index < running ? StepState.Done : StepState.Running))]) with
        {
            Phase = phase,
        };

    // Registers the service in Windows with the run token the agent in Windows PE handed over, and returns its token.
    private static async Task<string> ServiceTokenAsync(DeployingMachine machine) =>
        (await RegisteredMachine.ReadAsync<AgentRegistrationResult>(await machine.Agent.RegisterAsync(
            machine.Registration with { RunToken = machine.RunToken, Environment = AgentEnvironment.Windows }))).Token!;

    private DeploymentOptions Settings => application.Services.GetRequiredService<DdtSettings>().Current.Deployment;

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

        // The machine joins its domain later, in Windows. So the join account stays out of Panther\unattend.xml.
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

    // The templates of the answer file and the join are worked out from the run's values when the agent fetches them,
    // with the variables its steps set by then.
    [Fact]
    public async Task TheAnswerFileAndTheJoinWorkTheirTemplatesOutFromTheRunsValues()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        (DeployingMachine machine, AgentRun run) = await AssignedAsync(
            new WriteUnattendStep { Id = Guid.NewGuid(), Name = "Answer file", TimeZone = "{{Zone}}" },
            new JoinDomainStep { Id = Guid.NewGuid(), Name = "Join", OrganizationalUnit = "OU={{Site}},DC=corp,DC=example" });
        using DeployingMachine _ = machine;
        await ValuesAsync(administrator, machine, new NamedValue("Zone", "UTC"), new NamedValue("Site", "Lab"));

        await machine.ReportOkAsync(run.Id, Reached(run, 2));
        HttpResponseMessage answer = await machine.Agent.RunUnattendAsync(machine.Id, machine.Token, run.Id, run.Sequence.Steps[2].Id);
        XDocument xml = XDocument.Parse(await answer.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

        Assert.Equal("UTC", xml.Descendants(s_unattend + "TimeZone").Single().Value);
        Assert.Equal("PC-0005", xml.Descendants(s_unattend + "ComputerName").Single().Value);

        // A step changed the site since the run started.
        await machine.ReportOkAsync(
            run.Id,
            Reached(run, 3, SequencePhase.Windows) with { Variables = new Dictionary<string, string> { ["Site"] = "Kiosks" } });
        AgentJoinDomainCredentials credentials = await RegisteredMachine.ReadAsync<AgentJoinDomainCredentials>(
            await machine.Agent.RunCredentialsAsync(machine.Id, await ServiceTokenAsync(machine), run.Id, run.Sequence.Steps[3].Id));

        Assert.Equal("OU=Kiosks,DC=corp,DC=example", credentials.OrganizationalUnit);
    }

    // What the values produce is checked like the settings are, before a password leaves the server.
    [Fact]
    public async Task WhatWindowsWouldRefuseIsNotServed()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        (DeployingMachine machine, AgentRun run) = await AssignedAsync(
            new WriteUnattendStep { Id = Guid.NewGuid(), Name = "Answer file", TimeZone = "{{Zone}}", LocalAdministrator = true },
            new JoinDomainStep { Id = Guid.NewGuid(), Name = "Join", OrganizationalUnit = "OU={{Nowhere}},DC=corp,DC=example" });
        using DeployingMachine _ = machine;
        await ValuesAsync(administrator, machine, new NamedValue("Zone", "Mars Standard Time"));

        await machine.ReportOkAsync(run.Id, Reached(run, 2));
        HttpResponseMessage answer = await machine.Agent.RunUnattendAsync(machine.Id, machine.Token, run.Id, run.Sequence.Steps[2].Id);

        Assert.Equal(HttpStatusCode.Conflict, answer.StatusCode);
        Assert.Equal("The answer file's time zone, Mars Standard Time, is not a Windows time zone.", await TestDatabase.TitleAsync(answer));

        await machine.ReportOkAsync(run.Id, Reached(run, 3, SequencePhase.Windows));
        HttpResponseMessage join = await machine.Agent.RunCredentialsAsync(machine.Id, await ServiceTokenAsync(machine), run.Id, run.Sequence.Steps[3].Id);

        Assert.Equal(HttpStatusCode.Conflict, join.StatusCode);
        Assert.StartsWith(
            "The organizational unit OU={{Nowhere}},DC=corp,DC=example of step Join cannot be worked out from the run's values.",
            await TestDatabase.TitleAsync(join),
            StringComparison.Ordinal);
        Assert.Empty(await SecretReadsAsync(run.Id));
    }

    // A rule that sets these values for the machine alone.
    private static Task<RuleView> ValuesAsync(SignedInClient administrator, DeployingMachine machine, params NamedValue[] values) =>
        administrator.CreatedRuleAsync(RuleRequests.Rule(
            $"Values for {machine.Registration.PrimaryMac}",
            new TestCondition(MachineVariableNames.MacAddress, ConditionOperator.Equals, machine.Registration.PrimaryMac),
            values: values));

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

    // A server restart can remove a secret from the settings while the run continues. The step then gets nothing.
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

    // No password reaches the log at any point of a run, neither in the clear nor as the answer file encodes it.
    [Fact]
    public async Task NoPasswordIsEverLogged()
    {
        (DeployingMachine machine, AgentRun run) = await AssignedAsync();
        using DeployingMachine _ = machine;
        string[] secrets =
        [
            DomainDeploymentApplication.AdministratorPassword,
            Convert.ToBase64String(Encoding.Unicode.GetBytes(DomainDeploymentApplication.AdministratorPassword + "Password")),
            DomainDeploymentApplication.JoinPassword,
        ];

        await machine.ReportOkAsync(run.Id, Reached(run, 2));
        (await machine.Agent.RunUnattendAsync(machine.Id, machine.Token, run.Id, run.Sequence.Steps[2].Id)).EnsureSuccessStatusCode();
        await machine.ReportOkAsync(run.Id, Reached(run, 3, SequencePhase.Windows));
        (await machine.Agent.RunCredentialsAsync(machine.Id, await ServiceTokenAsync(machine), run.Id, run.Sequence.Steps[3].Id)).EnsureSuccessStatusCode();
        await machine.ReportOkAsync(run.Id, Report(DeploymentState.Done, [.. run.Sequence.Steps.Select(s => Step(s, StepState.Done))]) with { Phase = SequencePhase.Windows });

        Assert.Contains(application.Log.Entries, entry => entry.Message.Contains(run.Id.ToString("D"), StringComparison.Ordinal));
        Assert.All(application.Log.Entries, entry => Assert.All(secrets, secret =>
        {
            Assert.DoesNotContain(secret, entry.Message, StringComparison.Ordinal);
            Assert.DoesNotContain(secret, entry.Exception ?? string.Empty, StringComparison.Ordinal);
        }));
    }

    // The account is bound to the domain the run started with. So a domain configured since then gets nothing.
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
