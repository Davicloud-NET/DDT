// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Text.Json;
using DDT.Contracts.Agents;
using DDT.Contracts.Deployments;
using DDT.Contracts.Sequences;
using Xunit;

namespace DDT.Agent.Tests;

// The server and the agent are built against these; agents staged into Windows keep them for a whole run.
public sealed class AgentRunContractTests
{
    private const string MachineId = "0197a3c0-0000-7000-8000-00000000000a";
    private const string RunId = "0197a3c0-0000-7000-8000-00000000000b";
    private const string StepId = "0197a3c0-0000-7000-8000-00000000000c";

    [Fact]
    public void ReadsARegistrationFromAnAgentBeforeTaskSequences()
    {
        AgentRegistration? registration = JsonSerializer.Deserialize(
            """
            {"smbiosUuid":"4c4c4544-0042-3510-8052-b4c04f4d3232","primaryMac":"00155D010203","macAddresses":["00155D010203"],
             "manufacturer":null,"model":null,"serialNumber":null,"agentVersion":"1.0","resumeToken":null,"disks":null}
            """,
            AgentJsonContext.Default.AgentRegistration);

        Assert.NotNull(registration);
        Assert.Null(registration.RunToken);
        Assert.Equal(0, registration.SequenceVersion);
        Assert.Equal(AgentEnvironment.WindowsPE, registration.Environment);
    }

    [Fact]
    public void WritesTheRunMembersOfARegistration()
    {
        AgentRegistration registration = new(
            "4c4c4544-0042-3510-8052-b4c04f4d3232",
            "00155D010203",
            ["00155D010203"],
            null,
            null,
            null,
            "1.0",
            RunToken: "run-token",
            SequenceVersion: SequenceDefinition.CurrentVersion,
            Environment: AgentEnvironment.Windows,
            SecureBootEnabled: true);

        Assert.EndsWith(
            ""","runToken":"run-token","sequenceVersion":2,"environment":"Windows","secureBootEnabled":true}""",
            JsonSerializer.Serialize(registration, AgentJsonContext.Default.AgentRegistration),
            StringComparison.Ordinal);
    }

    [Fact]
    public void WritesAReportWithItsStepSnapshot()
    {
        AgentRunReport report = new(
            DeploymentState.Running,
            SequencePhase.WindowsPE,
            [new StepRunState(Guid.Parse(StepId), StepState.Running, null)],
            Guid.Parse(StepId),
            40,
            RunActivity.Step,
            null);

        Assert.Equal(
            $$"""{"state":"Running","phase":"WindowsPE","steps":[{"stepId":"{{StepId}}","state":"Running","error":null}],"currentStepId":"{{StepId}}","percent":40,"activity":"Step","error":null}""",
            JsonSerializer.Serialize(report, AgentJsonContext.Default.AgentRunReport));
    }

    [Fact]
    public void ReadsARunWithItsSequenceWhateverTheOrderOfItsProperties()
    {
        AgentRun? run = JsonSerializer.Deserialize(
            $$"""
            {"id":"{{RunId}}","state":"Assigned","sequenceName":"Install Windows",
             "sequence":{"version":1,"steps":[{"id":"{{StepId}}","name":"Apply","imageId":"{{RunId}}","kind":"applyImage"}]},
             "images":[{"imageId":"{{RunId}}","name":"Windows 11 Pro","sha256":"ab12","sizeBytes":5,"wimIndex":1,"installedBytes":9}],
             "packages":[],"diskNumber":null,"computerName":"PC-0042"}
            """,
            AgentJsonContext.Default.AgentRun);

        Assert.NotNull(run);
        ApplyImageStep apply = Assert.IsType<ApplyImageStep>(Assert.Single(run.Sequence.Steps));
        Assert.Equal(Guid.Parse(RunId), apply.ImageId);
        Assert.Equal(apply.ImageId, Assert.Single(run.Images).ImageId);
        Assert.Equal(DeploymentState.Assigned, run.State);
    }

    [Fact]
    public void KeepsTheJoinPasswordOutOfTheText()
    {
        AgentJoinDomainCredentials credentials = new("corp.example", "OU=Clients,DC=corp,DC=example", @"CORP\join", "Secret-Join-Password");

        string text = credentials.ToString();

        Assert.DoesNotContain("Secret-Join-Password", text, StringComparison.Ordinal);
        Assert.Contains(@"CORP\join", text, StringComparison.Ordinal);
        Assert.Contains("corp.example", text, StringComparison.Ordinal);
    }

    [Fact]
    public void NamesTheRunRoutes()
    {
        Guid machine = Guid.Parse(MachineId);
        Guid run = Guid.Parse(RunId);
        Guid step = Guid.Parse(StepId);

        Assert.Equal($"api/agents/{MachineId}/sequences", AgentRoutes.Sequences(machine));
        Assert.Equal($"api/agents/{MachineId}/runs", AgentRoutes.Runs(machine));
        Assert.Equal($"api/agents/{MachineId}/runs/{RunId}/report", AgentRoutes.RunReport(machine, run));
        Assert.Equal($"api/agents/{MachineId}/runs/{RunId}/files/ab12", AgentRoutes.RunFile(machine, run, "ab12"));
        Assert.Equal($"api/agents/{MachineId}/runs/{RunId}/steps/{StepId}/unattend", AgentRoutes.RunStepUnattend(machine, run, step));
        Assert.Equal($"api/agents/{MachineId}/runs/{RunId}/steps/{StepId}/credentials", AgentRoutes.RunStepCredentials(machine, run, step));
    }
}
