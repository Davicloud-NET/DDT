// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Text.Json;
using DDT.Contracts.Agents;
using DDT.Contracts.Machines;
using Xunit;

namespace DDT.Agent.Tests;

// What the change of boot environment added to the agent's contracts, which an older peer reads and writes as before.
public sealed class AgentSwitchContractTests
{
    private const string SequenceId = "0197a3c0-0000-7000-8000-00000000000d";

    private static AgentSequenceChoice Choice(AgentEnvironment? environment) =>
        new(Guid.Parse(SequenceId), "Install Linux", null, true, false, 0, false, Environment: environment);

    [Fact]
    public void APollAnswerWithoutARestartIsTheJsonItAlwaysWas()
    {
        string json = JsonSerializer.Serialize(new AgentNextResult(MachineState.Approved, "session", "resume", 10, null), AgentJsonContext.Default.AgentNextResult);

        Assert.DoesNotContain("restartInto", json, StringComparison.Ordinal);
        Assert.EndsWith(""","canPickSequence":false,"suggestedSequenceId":null}""", json, StringComparison.Ordinal);
    }

    [Fact]
    public void APollAnswerNamesTheBootEnvironmentToRestartInto()
    {
        AgentNextResult next = new(MachineState.Approved, "session", "resume", 10, null, RestartInto: AgentEnvironment.LinuxBoot);
        string json = JsonSerializer.Serialize(next, AgentJsonContext.Default.AgentNextResult);

        Assert.EndsWith(""","restartInto":"LinuxBoot"}""", json, StringComparison.Ordinal);
        Assert.Equal(AgentEnvironment.LinuxBoot, JsonSerializer.Deserialize(json, AgentJsonContext.Default.AgentNextResult)?.RestartInto);
    }

    [Fact]
    public void AChoiceNamesItsBootEnvironmentOnlyWhenTheServerKnowsIt()
    {
        string before = JsonSerializer.Serialize<IReadOnlyList<AgentSequenceChoice>>([Choice(null)], AgentJsonContext.Default.IReadOnlyListAgentSequenceChoice);
        string now = JsonSerializer.Serialize<IReadOnlyList<AgentSequenceChoice>>([Choice(AgentEnvironment.LinuxBoot)], AgentJsonContext.Default.IReadOnlyListAgentSequenceChoice);

        Assert.DoesNotContain("environment", before, StringComparison.Ordinal);
        Assert.EndsWith(""","environment":"LinuxBoot"}]""", now, StringComparison.Ordinal);
        Assert.Equal(
            AgentEnvironment.LinuxBoot,
            Assert.Single(JsonSerializer.Deserialize(now, AgentJsonContext.Default.IReadOnlyListAgentSequenceChoice)!).Environment);
    }

    [Fact]
    public void TheRequestNamesTheEnvironmentAndTheSequencePickedForIt()
    {
        Assert.Equal(
            $$"""{"environment":"LinuxBoot","sequenceId":"{{SequenceId}}"}""",
            JsonSerializer.Serialize(new AgentEnvironmentRequest(AgentEnvironment.LinuxBoot, Guid.Parse(SequenceId)), AgentJsonContext.Default.AgentEnvironmentRequest));
        Assert.Equal(
            """{"environment":"WindowsPE"}""",
            JsonSerializer.Serialize(new AgentEnvironmentRequest(AgentEnvironment.WindowsPE), AgentJsonContext.Default.AgentEnvironmentRequest));
    }

    [Fact]
    public void TheRouteIsTheMachinesOwn()
    {
        Assert.Equal(
            "api/agents/0197a3c0-0000-7000-8000-00000000000a/environment",
            AgentRoutes.Environment(Guid.Parse("0197a3c0-0000-7000-8000-00000000000a")));
    }
}
