// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using DDT.Contracts.Agents;
using DDT.Contracts.Deployments;
using DDT.Contracts.Sequences;
using DDT.Server.Deployments;
using Microsoft.EntityFrameworkCore;
using Xunit;
using static DDT.Server.Tests.TestReports;

namespace DDT.Server.Tests;

// The answer file learns from the applied image whether Windows Server's built-in Administrator needs a password.
public sealed class ServerAnswerFileTests(DomainDeploymentApplication application) : IClassFixture<DomainDeploymentApplication>
{
    private static readonly XNamespace s_unattend = "urn:schemas-microsoft-com:unattend";

    private async Task<XDocument> AnswerFileAsync(string edition)
    {
        SignedInClient administrator = await application.AdministratorAsync();
        using DeployingMachine machine = await DeployingMachine.ApprovedAsync(application, administrator);
        Guid imageId = (await application.SeedImageAsync(RandomNumberGenerator.GetBytes(4096))).Id;
        await application.QueryAsync(database => database.Images
            .Where(image => image.Id == imageId)
            .ExecuteUpdateAsync(set => set.SetProperty(image => image.Edition, edition), TestContext.Current.CancellationToken));
        WriteUnattendStep unattend = new() { Id = Guid.NewGuid(), Name = "Answer file", LocalAdministrator = true };
        SequenceView sequence = await administrator.CreatedSequenceAsync(SequenceRequests.Definition([.. SequenceRequests.Minimal(imageId).Steps, unattend]));
        await administrator.AssignedAsync(machine.Id, sequence.Id, "PC-0007");
        AgentRun run = (await machine.NextAsync()).Run!;

        await machine.ReportOkAsync(run.Id, Report(
            DeploymentState.Running,
            [.. run.Sequence.Steps.Take(3).Select((step, index) => Step(step, index < 2 ? StepState.Done : StepState.Running))]));
        HttpResponseMessage response = await machine.Agent.RunUnattendAsync(machine.Id, machine.Token, run.Id, unattend.Id);
        response.EnsureSuccessStatusCode();

        return XDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task AServerImageGetsTheBuiltInAdministratorsPassword()
    {
        XDocument answer = await AnswerFileAsync("ServerDatacenter");

        Assert.Equal(
            Convert.ToBase64String(Encoding.Unicode.GetBytes(DomainDeploymentApplication.AdministratorPassword + "AdministratorPassword")),
            answer.Descendants(s_unattend + "AdministratorPassword").Single().Element(s_unattend + "Value")?.Value);
        Assert.Single(answer.Descendants(s_unattend + "LocalAccount"));
    }

    [Fact]
    public async Task AClientImageDoesNot()
    {
        XDocument answer = await AnswerFileAsync("Professional");

        Assert.Empty(answer.Descendants(s_unattend + "AdministratorPassword"));
        Assert.Single(answer.Descendants(s_unattend + "LocalAccount"));
    }

    [Theory]
    [InlineData("ServerStandard", true)]
    [InlineData("ServerDatacenterEval", true)]
    [InlineData("serverazureedition", true)]
    [InlineData("Professional", false)]
    [InlineData("Enterprise", false)]
    [InlineData(null, false)]
    public void TellsAServerEditionByItsEditionId(string? edition, bool server) => Assert.Equal(server, UnattendRenderer.IsServer(edition));
}
