// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Security.Cryptography;
using DDT.Contracts.Agents;
using DDT.Contracts.Rules;
using DDT.Contracts.Sequences;
using DDT.Contracts.Values;

namespace DDT.Server.Tests;

// Rules and the machines they match, each test with a model of its own, so the rules of one never match another's. A
// test class shares one server, so no test adds a rule without a condition, which would match every machine.
internal static class RuleRequests
{
    public const string Rules = "/api/rules";

    public const string Roles = "/api/machine-roles";

    public static string UniqueModel() => $"Model {Guid.NewGuid():N}";

    public static string RandomMac() => "02" + Convert.ToHexString(RandomNumberGenerator.GetBytes(5));

    // What an assignment rule by MAC address became: a test of every address the machine reported.
    public static SaveRuleRequest MacRule(Guid? sequenceId, string mac) =>
        Rule($"MAC address {mac}", new TestCondition(MachineVariableNames.MacAddress, ConditionOperator.Equals, mac), sequenceId);

    // What an assignment rule by model became: the manufacturer where one is named, and the model whole or, ending in *,
    // as the start of the model.
    public static SaveRuleRequest ModelRule(Guid? sequenceId, string model, string? manufacturer = null)
    {
        TestCondition byModel = new(
            MachineVariableNames.Model,
            model.TrimEnd().EndsWith('*') ? ConditionOperator.Matches : ConditionOperator.Equals,
            model);

        return Rule(
            $"Model {model}",
            new AllCondition
            {
                Parts = manufacturer is null
                    ? [byModel]
                    : [new TestCondition(MachineVariableNames.Manufacturer, ConditionOperator.Equals, manufacturer), byModel],
            },
            sequenceId);
    }

    public static SaveRuleRequest Rule(
        string name,
        ConditionNode? when,
        Guid? sequenceId = null,
        IReadOnlyList<NamedValue>? values = null,
        IReadOnlyList<Guid>? roleIds = null) =>
        new(0, name, null, true, when, sequenceId, values ?? [], roleIds ?? []);

    public static async Task<RuleView> CreatedRuleAsync(this SignedInClient client, SaveRuleRequest rule) =>
        await RegisteredMachine.ReadAsync<RuleView>(await client.PostAsync(Rules, rule));

    public static async Task<MachineRoleView> CreatedRoleAsync(this SignedInClient client, SaveMachineRoleRequest role) =>
        await RegisteredMachine.ReadAsync<MachineRoleView>(await client.PostAsync(Roles, role));

    public static async Task<IReadOnlyList<RuleView>> RulesAsync(this SignedInClient client) =>
        await RegisteredMachine.ReadAsync<IReadOnlyList<RuleView>>(await client.GetAsync(Rules));

    public static Task<HttpResponseMessage> ReorderAsync(this SignedInClient client, IReadOnlyList<Guid> ruleIds) =>
        client.PostAsync($"{Rules}/order", new ReorderRulesRequest(ruleIds));

    // The whole list in a new order, as the page sends it: the rules of other tests stay where they are, and moved goes
    // right above before.
    public static async Task<IReadOnlyList<RuleView>> MoveRuleAboveAsync(this SignedInClient client, Guid moved, Guid before)
    {
        List<Guid> order = [.. (await client.RulesAsync()).Select(r => r.Id)];
        order.Remove(moved);
        order.Insert(order.IndexOf(before), moved);

        return await RegisteredMachine.ReadAsync<IReadOnlyList<RuleView>>(await client.ReorderAsync(order));
    }

    // A save of the rule as it is, changed by change.
    public static SaveRuleRequest Save(RuleView rule, Func<SaveRuleRequest, SaveRuleRequest>? change = null)
    {
        ArgumentNullException.ThrowIfNull(rule);

        SaveRuleRequest save = new(rule.Revision, rule.Name, rule.Description, rule.Enabled, rule.When, rule.SequenceId, rule.Values, rule.RoleIds);

        return change is null ? save : change(save);
    }

    public static async Task<SequenceView> RunnableSequenceAsync(this DdtApplication application)
    {
        ArgumentNullException.ThrowIfNull(application);

        Guid imageId = (await application.SeedImageAsync(RandomNumberGenerator.GetBytes(4096))).Id;

        return await (await application.AdministratorAsync()).CreatedSequenceAsync(SequenceRequests.Minimal(imageId));
    }

    public static async Task<MachineSequenceResolution> ResolutionAsync(this SignedInClient client, Guid machineId) =>
        await RegisteredMachine.ReadAsync<MachineSequenceResolution>(await client.GetAsync($"/api/machines/{machineId}/sequence"));

    // A machine that registers as change makes the test agent's registration say.
    public static async Task<RegisteredMachine> RegisterWithAsync(this DdtApplication application, Func<AgentRegistration, AgentRegistration> change)
    {
        ArgumentNullException.ThrowIfNull(application);
        ArgumentNullException.ThrowIfNull(change);

        AgentClient agent = new(application.CreateDefaultClient(), TestRemoteAddress.Unique());
        AgentRegistration registration = change(AgentClient.Registration(Guid.NewGuid().ToString("D"), RandomMac()));
        AgentRegistrationResult registered = await RegisteredMachine.ReadAsync<AgentRegistrationResult>(await agent.RegisterAsync(registration));

        return new RegisteredMachine(agent, registration, registered);
    }

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
