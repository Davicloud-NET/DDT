// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Rules;
using DDT.Contracts.Sequences;
using DDT.Contracts.Values;
using Xunit;

namespace DDT.Server.Tests;

// A sequence may use a name only rules and machine roles give a value, which the validator leaves to the server: it warns
// of one that nothing gives a value.
public sealed class SequenceValueTests(DdtApplication application) : IClassFixture<DdtApplication>
{
    private static string UniqueName(string prefix) => $"{prefix}{Guid.NewGuid():N}"[..24];

    private static IEnumerable<string> Undefined(SequenceView view) =>
        view.Warnings.Where(warning => warning.Code == "sequence.valueUndefined").Select(warning => warning.Args!["name"].ToString()!);

    [Fact]
    public async Task WarnsOfANameNoRuleNorMachineRoleGivesAValue()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        string department = UniqueName("Department");
        string building = UniqueName("Building");
        RunScriptStep script = (RunScriptStep)SequenceRequests.ScriptOnly().Steps[0] with
        {
            When = new TestCondition(department, ConditionOperator.Equals, "Finance"),
        };
        PauseStep pause = TreeSequences.Pause($"Take it to {{{{{building}}}}} and {{{{TimeZone}}}}, as {{{{AdministratorName}}}}.");

        SequenceView sequence = await administrator.CreatedSequenceAsync(SequenceRequests.Definition(script, pause));

        Assert.Empty(sequence.Problems);
        Assert.Equal([department, building], Undefined(sequence));
        SequenceProblem warning = Assert.Single(sequence.Warnings, w => w.Code == "sequence.valueUndefined" && w.Args!["name"].ToString() == department);
        Assert.Equal((null, null), (warning.StepId, warning.Field));
        Assert.Equal(
            $"{department} is used, but neither the sequence nor a rule or a machine role gives it a value. A run fails where it needs it.",
            warning.Message);

        // A rule, even one that is off, and a machine role give them values.
        SaveRuleRequest finance = RuleRequests.Rule(
            "Finance",
            new TestCondition(MachineVariableNames.MacAddress, ConditionOperator.Equals, RuleRequests.RandomMac()),
            values: [new NamedValue(department, "Finance")]);
        await administrator.CreatedRuleAsync(finance with { Enabled = false });
        await administrator.CreatedRoleAsync(new SaveMachineRoleRequest(0, UniqueName("Role"), null, [new NamedValue(building, "B 12")]));

        SequenceView again = await RegisteredMachine.ReadAsync<SequenceView>(await administrator.GetAsync($"{SequenceRequests.Sequences}/{sequence.Id}"));
        Assert.Empty(Undefined(again));
    }
}
