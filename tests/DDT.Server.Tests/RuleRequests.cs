// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Security.Cryptography;
using DDT.Contracts.Agents;
using DDT.Contracts.Rules;
using DDT.Contracts.Sequences;

namespace DDT.Server.Tests;

// Rules and the machines they match, each test with a model of its own, so the rules of one never match another's.
internal static class RuleRequests
{
    public const string Rules = "/api/rules";

    public static string UniqueModel() => $"Model {Guid.NewGuid():N}";

    public static string RandomMac() => "02" + Convert.ToHexString(RandomNumberGenerator.GetBytes(5));

    public static SaveAssignmentRuleRequest MacRule(Guid sequenceId, string mac) =>
        new(AssignmentRuleKind.Mac, mac, null, null, sequenceId, null);

    public static SaveAssignmentRuleRequest ModelRule(Guid sequenceId, string model, string? manufacturer = null) =>
        new(AssignmentRuleKind.Model, null, manufacturer, model, sequenceId, null);

    public static async Task<AssignmentRuleView> CreatedRuleAsync(this SignedInClient client, SaveAssignmentRuleRequest rule) =>
        await RegisteredMachine.ReadAsync<AssignmentRuleView>(await client.PostAsync(Rules, rule));

    public static async Task<SequenceView> RunnableSequenceAsync(this DdtApplication application)
    {
        ArgumentNullException.ThrowIfNull(application);

        Guid imageId = (await application.SeedImageAsync(RandomNumberGenerator.GetBytes(4096))).Id;

        return await (await application.AdministratorAsync()).CreatedSequenceAsync(SequenceRequests.Minimal(imageId));
    }

    public static async Task<MachineSequenceResolution> ResolutionAsync(this SignedInClient client, Guid machineId) =>
        await RegisteredMachine.ReadAsync<MachineSequenceResolution>(await client.GetAsync($"/api/machines/{machineId}/sequence"));

    // A machine that reports this maker and model, and these MAC addresses with the first as its primary one.
    public static async Task<RegisteredMachine> RegisterModelAsync(
        this DdtApplication application,
        string? manufacturer,
        string? model,
        string? remoteAddress = null,
        params string[] macs)
    {
        ArgumentNullException.ThrowIfNull(application);

        string[] addresses = macs.Length > 0 ? macs : [RandomMac()];
        AgentClient agent = new(application.CreateDefaultClient(), remoteAddress ?? TestRemoteAddress.Unique());
        AgentRegistration registration = AgentClient.Registration(Guid.NewGuid().ToString("D"), addresses[0], addresses[1..]) with
        {
            Manufacturer = manufacturer,
            Model = model,
        };
        AgentRegistrationResult registered = await RegisteredMachine.ReadAsync<AgentRegistrationResult>(await agent.RegisterAsync(registration));

        return new RegisteredMachine(agent, registration, registered);
    }
}
