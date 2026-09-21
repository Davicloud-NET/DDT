// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using DDT.Contracts.Agents;
using DDT.Contracts.Deployments;
using DDT.Contracts.Machines;
using DDT.Server.Authentication;
using DDT.Server.Images;
using Xunit;

namespace DDT.Server.Tests;

public sealed class DomainDeploymentTests(DomainDeploymentApplication application) : IClassFixture<DomainDeploymentApplication>
{
    private static readonly XNamespace s_unattend = "urn:schemas-microsoft-com:unattend";

    private Task<Image> ImageAsync() => application.SeedImageAsync(RandomNumberGenerator.GetBytes(4096));

    private async Task<HttpResponseMessage> AssignAsync(Guid machineId, Guid imageId, string? computerName) =>
        await (await application.AdministratorAsync()).PostAsync(
            $"/api/machines/{machineId}/deployments",
            new AssignImageRequest(imageId, computerName));

    [Fact]
    public async Task TheAgentAndTheWebUiLearnThatADomainIsConfigured()
    {
        string operatorName = await application.CreateUserAsync(DdtRoleNames.Operator);
        using DeployingMachine machine = await DeployingMachine.SignedInAsync(application, operatorName);
        SignedInClient administrator = await application.AdministratorAsync();

        AgentNextResult next = await machine.NextAsync();

        Assert.True(next.DomainConfigured);
        Assert.Null(next.AssignedName);
        Assert.True((await RegisteredMachine.ReadAsync<DeploymentOptionsView>(await administrator.GetAsync("/api/deployments/options"))).DomainConfigured);
    }

    // Anyone who presents a waiting machine's UUID and MAC polls as it, so it learns neither its name nor the domain.
    [Fact]
    public async Task AWaitingMachineLearnsItsNameAndTheDomainOnlyOnceAuthorized()
    {
        using DeployingMachine machine = await DeployingMachine.RegisterAsync(application);
        Image image = await ImageAsync();

        await application.ChangeMachineAsync(machine.Id, m => m.LastSeenUtc = DateTimeOffset.UtcNow - TimeSpan.FromMinutes(10));
        Assert.Equal(MachineState.Pending, (await RegisteredMachine.ReadAsync<MachineSummary>(await AssignAsync(machine.Id, image.Id, "PC-0006"))).State);

        AgentNextResult waiting = await machine.NextAsync();

        Assert.Equal(MachineState.Pending, waiting.State);
        Assert.False(waiting.DomainConfigured);
        Assert.Null(waiting.AssignedName);
        Assert.Null(waiting.Deployment);

        string operatorName = await application.CreateUserAsync(DdtRoleNames.Operator);
        AgentSignInResult signedIn = await RegisteredMachine.ReadAsync<AgentSignInResult>(
            await machine.Agent.SignInAsync(machine.Id, machine.Token, new AgentSignInRequest(operatorName, DdtApplication.Password, null)));

        Assert.Equal(AgentSignInStatus.Succeeded, signedIn.Status);

        AgentNextResult approved = await machine.NextAsync();

        Assert.Equal(MachineState.Approved, approved.State);
        Assert.True(approved.DomainConfigured);
        Assert.Equal("PC-0006", approved.AssignedName);
        Assert.NotNull(approved.Deployment);
    }

    [Fact]
    public async Task AMachineJoiningTheDomainNeedsAName()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        using DeployingMachine machine = await DeployingMachine.ApprovedAsync(application, administrator);
        Image image = await ImageAsync();

        HttpResponseMessage nameless = await AssignAsync(machine.Id, image.Id, null);
        Assert.Equal(HttpStatusCode.BadRequest, nameless.StatusCode);
        Assert.Contains("computer name", await nameless.Content.ReadAsStringAsync(TestContext.Current.CancellationToken), StringComparison.Ordinal);

        MachineSummary named = await RegisteredMachine.ReadAsync<MachineSummary>(await AssignAsync(machine.Id, image.Id, " PC-0003 "));
        Assert.Equal("PC-0003", named.AssignedName);

        // Once it has a name, a later assignment keeps it.
        (await administrator.DeleteAsync($"/api/machines/{machine.Id}/deployments/current")).EnsureSuccessStatusCode();
        Assert.Equal("PC-0003", (await RegisteredMachine.ReadAsync<MachineSummary>(await AssignAsync(machine.Id, image.Id, null))).AssignedName);
    }

    [Fact]
    public async Task AMachineJoiningTheDomainNeedsANameWhenChosenAtTheMachine()
    {
        string operatorName = await application.CreateUserAsync(DdtRoleNames.Operator);
        using DeployingMachine machine = await DeployingMachine.SignedInAsync(application, operatorName);
        Image image = await ImageAsync();

        Assert.Equal(
            HttpStatusCode.BadRequest,
            (await machine.Agent.PickAsync(machine.Id, machine.Token, new AgentPickRequest(image.Id, 0, null))).StatusCode);

        AgentDeployment picked = await RegisteredMachine.ReadAsync<AgentDeployment>(
            await machine.Agent.PickAsync(machine.Id, machine.Token, new AgentPickRequest(image.Id, 0, "PC-0004")));

        Assert.Equal(DeploymentState.Assigned, picked.State);
        Assert.Equal("PC-0004", (await machine.NextAsync()).AssignedName);
    }

    [Fact]
    public async Task OnlyARunningDeploymentGetsItsAnswerFile()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        using DeployingMachine machine = await DeployingMachine.ApprovedAsync(application, administrator);
        Image image = await ImageAsync();

        Guid deployment = (await RegisteredMachine.ReadAsync<MachineSummary>(await AssignAsync(machine.Id, image.Id, "PC-0005"))).Deployment!.Id;

        HttpResponseMessage early = await machine.Agent.UnattendAsync(machine.Id, machine.Token, deployment);
        Assert.Equal(HttpStatusCode.Conflict, early.StatusCode);

        await machine.ReportOkAsync(deployment, DeploymentState.Running, DeploymentStep.Partition);

        HttpResponseMessage response = await machine.Agent.UnattendAsync(machine.Id, machine.Token, deployment);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/xml", response.Content.Headers.ContentType?.MediaType);
        Assert.True(response.Headers.CacheControl?.NoStore);

        XDocument answer = XDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        string Value(string name) => answer.Descendants(s_unattend + name).Single().Value;

        Assert.Equal("PC-0005", Value("ComputerName"));
        Assert.Equal("W. Europe Standard Time", Value("TimeZone"));
        Assert.Equal("en-US", Value("UILanguage"));
        Assert.Equal("en-US", Value("SystemLocale"));
        Assert.Equal("0407:00000407", Value("InputLocale"));
        Assert.Equal("corp.example", Value("JoinDomain"));
        Assert.Equal("OU=Workstations,DC=corp,DC=example", Value("MachineObjectOU"));
        Assert.Equal(@"CORP\ddt-join", Value("Username"));
        Assert.Equal(DomainDeploymentApplication.JoinPassword, answer.Descendants(s_unattend + "Credentials").Single().Element(s_unattend + "Password")?.Value);
        Assert.Equal("Admin", answer.Descendants(s_unattend + "LocalAccount").Single().Element(s_unattend + "Name")?.Value);
        Assert.Equal(
            Convert.ToBase64String(Encoding.Unicode.GetBytes(DomainDeploymentApplication.AdministratorPassword + "Password")),
            answer.Descendants(s_unattend + "LocalAccount").Single().Descendants(s_unattend + "Value").Single().Value);
        Assert.All(answer.Descendants().Where(e => e.Name.LocalName == "component"), component =>
            Assert.Equal("amd64", component.Attribute("processorArchitecture")?.Value));

        // Another machine's token never reaches this machine's answer file.
        using DeployingMachine other = await DeployingMachine.ApprovedAsync(application, administrator);
        Assert.Equal(HttpStatusCode.Forbidden, (await other.Agent.UnattendAsync(machine.Id, other.Token, deployment)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await other.Agent.UnattendAsync(other.Id, other.Token, deployment)).StatusCode);

        await machine.ReportOkAsync(deployment, DeploymentState.Failed, DeploymentStep.Boot, 0, "bcdboot failed.");

        Assert.Equal(HttpStatusCode.Conflict, (await machine.Agent.UnattendAsync(machine.Id, machine.Token, deployment)).StatusCode);
    }
}
