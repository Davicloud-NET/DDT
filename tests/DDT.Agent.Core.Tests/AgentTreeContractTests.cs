// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using DDT.Contracts.Accounts;
using DDT.Contracts.Agents;
using DDT.Contracts.Deployments;
using DDT.Contracts.Machines;
using DDT.Contracts.Sequences;
using DDT.Contracts.Settings;
using Xunit;

namespace DDT.Agent.Tests;

// What version 3 sequences add to the agent's wire format: members at the end that older readers never see while unset,
// the answers to inputs, and the accounts a step uses. Their passwords never reach a log.
public sealed class AgentTreeContractTests
{
    private const string MachineId = "0197a3c0-0000-7000-8000-00000000000a";
    private const string RunId = "0197a3c0-0000-7000-8000-00000000000b";
    private const string StepId = "0197a3c0-0000-7000-8000-00000000000c";
    private const string Password = "Secret-Account-Password";

    private static readonly AgentInput s_office = new(
        "Office",
        "Office edition",
        "Which Office the PC gets.",
        InputKind.Choice,
        [new InputChoice("Standard"), new InputChoice("ProPlus", "Professional Plus")],
        "Standard",
        true,
        null);

    [Fact]
    public void NamesTheRoutesOfAnswersAndAccounts()
    {
        Guid machine = Guid.Parse(MachineId);
        Guid run = Guid.Parse(RunId);
        Guid step = Guid.Parse(StepId);

        Assert.Equal($"api/agents/{MachineId}/runs/{RunId}/answers", AgentRoutes.RunAnswers(machine, run));
        Assert.Equal($"api/agents/{MachineId}/runs/{RunId}/steps/{StepId}/accounts", AgentRoutes.RunStepAccounts(machine, run, step));
    }

    [Fact]
    public void KeepsPasswordsOutOfTheText()
    {
        object[] holders =
        [
            new InputAnswer("JoinAccount", null, @"CORP\join", Password),
            new AgentAccount(@"CORP\installer", Password),
            new AgentShareConnection(@"\\files.corp.example\drivers", @"CORP\reader", Password),
            new AgentStepAccounts(new AgentAccount(@"CORP\installer", Password), [new AgentShareConnection(@"\\files\drivers", "reader", Password)]),
            new AgentInputAnswers([new InputAnswer("JoinAccount", null, @"CORP\join", Password)]),
            new AgentRunRequest(Guid.Parse(RunId), null, null, Answers: [new InputAnswer("JoinAccount", null, "join", Password)]),
            new SaveAccountRequest(3, "Installer", @"CORP\installer", "corp.example", ["files.corp.example"], true, new SecretUpdate(SecretAction.Set, Password)),
        ];

        foreach (object holder in holders)
        {
            Assert.DoesNotContain(Password, holder.ToString(), StringComparison.Ordinal);
        }

        Assert.Contains(@"CORP\join", holders[0].ToString(), StringComparison.Ordinal);
        Assert.Contains(@"\\files.corp.example\drivers", holders[2].ToString(), StringComparison.Ordinal);
        Assert.Contains("Password = Set", holders[^1].ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void WritesTheFactsAtTheEndOfARegistration()
    {
        AgentRegistration registration = new(
            "4c4c4544-0042-3510-8052-b4c04f4d3232",
            "00155D010203",
            ["00155D010203"],
            null,
            null,
            null,
            "1.0",
            ChassisType: 10,
            Facts: new MachineFacts { MemoryMegabytes = 16384, TpmPresent = true, TpmVersion = "2.0", IPv4Address = "10.0.0.23" });

        string json = JsonSerializer.Serialize(registration, AgentJsonContext.Default.AgentRegistration);

        Assert.Contains(""","chassisType":10,"facts":{"memoryMegabytes":16384,"processorName":null,""", json, StringComparison.Ordinal);
        Assert.Equal(registration.Facts, JsonSerializer.Deserialize(json, AgentJsonContext.Default.AgentRegistration)?.Facts);
    }

    [Fact]
    public void WritesAndReadsTheRunMembersOfVersion3()
    {
        AgentRun run = new(
            Guid.Parse(RunId),
            DeploymentState.Assigned,
            "Install Windows",
            new SequenceDefinition(3, [new PauseStep { Id = Guid.Parse(StepId), Name = "Check" }]),
            [],
            [],
            null,
            "PC-0042",
            Values: new Dictionary<string, string>(StringComparer.Ordinal) { ["Office"] = "ProPlus" },
            PendingInputs: [s_office]);
        AgentRunReport report = new(
            DeploymentState.Running,
            SequencePhase.WindowsPE,
            [new StepRunState(Guid.Parse(StepId), StepState.Running, null, Pass: 2)],
            Guid.Parse(StepId),
            0,
            RunActivity.Paused,
            null,
            new Dictionary<string, string>(StringComparer.Ordinal) { ["Office"] = "ProPlus" },
            "Check the BIOS of PC-0042.");
        AgentRunReportResult result = new("session", "resume", "run", null, [s_office], Guid.Parse(StepId), 2, 5);
        AgentSequenceChoice choice = new(Guid.Parse(RunId), "Install Windows", null, true, true, 1, false, Inputs: [s_office]);
        AgentAnswersResult answers = new(null, [s_office], [new InputProblem("Office", "Choose one of Standard and ProPlus.")]);
        AgentStepAccounts accounts = new(null, [new AgentShareConnection(@"\\files\drivers", "reader", Password)]);

        AssertRoundTrip(run, AgentJsonContext.Default.AgentRun, """
            "values":{"Office":"ProPlus"},"pendingInputs":[{"name":"Office","label":"Office edition",
            """);
        AssertRoundTrip(report, AgentJsonContext.Default.AgentRunReport, """
            "activity":"Paused","error":null,"variables":{"Office":"ProPlus"},"pauseMessage":"Check the BIOS of PC-0042."}
            """);
        AssertRoundTrip(result, AgentJsonContext.Default.AgentRunReportResult, """
            "runToken":"run","inputsPending":[
            """);
        AssertRoundTrip(result, AgentJsonContext.Default.AgentRunReportResult, $$"""
            "continueStepId":"{{StepId}}","continuePass":2,"reportAfterSeconds":5}
            """);
        AssertRoundTrip<IReadOnlyList<AgentSequenceChoice>>([choice], AgentJsonContext.Default.IReadOnlyListAgentSequenceChoice, """
            "inputs":[{"name":"Office"
            """);
        AssertRoundTrip(answers, AgentJsonContext.Default.AgentAnswersResult, """
            "problems":[{"name":"Office","message":"Choose one of Standard and ProPlus."}]}
            """);
        AssertRoundTrip(accounts, AgentJsonContext.Default.AgentStepAccounts, """
            {"runAs":null,"shares":[{"path":"\\\\files\\drivers","userName":"reader","password":"Secret-Account-Password"}]}
            """);
    }

    // What a server older than version 3 sends is read with every new member unset.
    [Fact]
    public void ReadsARunAndAResultWithoutTheMembersOfVersion3()
    {
        AgentRunReportResult? result = JsonSerializer.Deserialize(
            """{"token":"session","resumeToken":"resume","runToken":null}""",
            AgentJsonContext.Default.AgentRunReportResult);

        Assert.Equal(new AgentRunReportResult("session", "resume", null), result);
    }

    // part is a piece of the JSON the value is written as. Reading the value back writes the same JSON.
    private static void AssertRoundTrip<T>(T value, JsonTypeInfo<T> typeInfo, string part)
    {
        string json = JsonSerializer.Serialize(value, typeInfo);

        Assert.Contains(part, json, StringComparison.Ordinal);
        Assert.Equal(json, JsonSerializer.Serialize(JsonSerializer.Deserialize(json, typeInfo)!, typeInfo));
    }
}
