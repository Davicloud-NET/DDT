// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Channels;
using DDT.Contracts;
using DDT.Contracts.Rules;
using DDT.Contracts.Sequences;
using DDT.Contracts.Values;
using DDT.Server.Authentication;
using DDT.Server.Data;
using DDT.Server.Live;
using DDT.Server.Machines;
using DDT.Server.Rules;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DDT.Server.Tests;

// The ordered rules as the Rules page reads and writes them. The tests of a class share one server, and the rules of
// every test there are in one list, so a test finds its own rules by id.
public sealed class RuleEndpointTests(DdtApplication application) : IClassFixture<DdtApplication>
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private Task<List<AuditEvent>> AuditAsync(string action) =>
        application.QueryAsync(database => database.AuditEvents
            .AsNoTracking()
            .Where(e => e.Action == action)
            .OrderBy(e => e.Id)
            .ToListAsync(Cancellation));

    private Task<List<AuditEvent>> AuditAsync(Guid subject)
    {
        string id = subject.ToString("D");

        return application.QueryAsync(database => database.AuditEvents.AsNoTracking().Where(e => e.SubjectId == id).OrderBy(e => e.Id).ToListAsync(Cancellation));
    }

    private static string Json(ConditionNode? node) => JsonSerializer.Serialize(node, DdtJsonContext.Default.ConditionNode);

    private static async Task<T> BodyAsync<T>(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<T>(TestJson.Options, Cancellation))!;

    private async Task<IEnumerable<string>> RefusedFieldsAsync(SaveRuleRequest rule)
    {
        HttpResponseMessage response = await (await application.AdministratorAsync()).PostAsync(RuleRequests.Rules, rule);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        return (await BodyAsync<ValidationProblemDetails>(response)).Errors.Keys;
    }

    // The positions go from 0 at the top without a gap, whatever was added, moved or deleted.
    private static void AssertNumbered(IReadOnlyList<RuleView> rules) => Assert.Equal(Enumerable.Range(0, rules.Count), rules.Select(r => r.Position));

    [Fact]
    public async Task AddsARuleAtTheBottomAndPushesTheWholeList()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        using SignedInClient viewer = await application.SignInAsync(DdtRoleNames.Viewer);
        await using LiveListener live = await LiveListener.StartAsync(application, viewer);
        ChannelReader<RuleView[]> pushes = live.Listen<RuleView[]>(LiveEvents.RulesChanged);
        SequenceView sequence = await application.RunnableSequenceAsync();
        string model = RuleRequests.UniqueModel();
        ConditionNode when = new AllCondition
        {
            Parts =
            [
                new TestCondition(MachineVariableNames.Manufacturer, ConditionOperator.Equals, "Dell Inc."),
                new TestCondition(MachineVariableNames.Model, ConditionOperator.Matches, $"{model}*"),
            ],
        };

        HttpResponseMessage response = await administrator.PostAsync(
            RuleRequests.Rules,
            new SaveRuleRequest(
                42,
                "  Dell\0 laptops ",
                " For the lab ",
                true,
                when,
                sequence.Id,
                [new NamedValue(" ComputerName ", "PC-{{SerialNumber|alnum|right:12}}\0")],
                []));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        RuleView created = await BodyAsync<RuleView>(response);
        IReadOnlyList<RuleView> listed = await viewer.RulesAsync();

        Assert.Equal($"/api/rules/{created.Id}", response.Headers.Location?.OriginalString);
        Assert.Equal(
            ("Dell laptops", "For the lab", true, sequence.Id, sequence.Name, 1L, "administrator-"),
            (created.Name, created.Description, created.Enabled, created.SequenceId, created.SequenceName, created.Revision, created.UpdatedBy![..14]));
        Assert.Equal(Json(when), Json(created.When));
        Assert.Equal([new NamedValue("ComputerName", "PC-{{SerialNumber|alnum|right:12}}")], created.Values);
        Assert.Empty(created.RoleIds);
        Assert.Empty(created.Problems);
        Assert.Equal(0, created.MatchingMachines);
        Assert.Equal(created.Id, listed[^1].Id);
        Assert.Equal(listed.Count - 1, created.Position);
        AssertNumbered(listed);

        RuleView[] pushed = await LiveListener.NextAsync(pushes, rules => rules.Any(r => r.Id == created.Id));
        Assert.Equal(listed.Select(r => (r.Id, r.Position, r.Revision)), pushed.Select(r => (r.Id, r.Position, r.Revision)));

        AuditEvent audit = Assert.Single(await AuditAsync(created.Id));
        Assert.Equal((AuditActions.RuleCreated, $"Rule {created.Position + 1}, Dell laptops."), (audit.Action, audit.Detail));
    }

    // The code of each of EverythingWrong's problems, in the order of their fields.
    private static readonly string[] EverythingWrongCodes =
    [
        "rule.conditionUnknownName",
        "rule.conditionOperatorType",
        "mac.enterFull",
        "mac.enterPart",
        "rule.conditionSubnet",
        "rule.conditionNumber",
        "rule.conditionYesNo",
        "rule.conditionAddress",
        "rule.conditionRunVariable",
        "rule.conditionChooseName",
        "sequence.conditionValue",
        "namedValue.nameInvalid",
        "values.fact",
        "namedValue.reserved",
        "namedValue.repeated",
        "valueTemplate.unknownFilter",
        "rule.roleGone",
    ];

    // A condition, values and a role with a problem in every place one can be.
    private static SaveRuleRequest EverythingWrong(Guid gone) =>
        RuleRequests.Rule(
            "Everything wrong",
            new AllCondition
            {
                Parts =
                [
                    new TestCondition("NoSuchValue", ConditionOperator.Equals, "x"),
                    new TestCondition(MachineVariableNames.Model, ConditionOperator.Greater, "5"),
                    new TestCondition(MachineVariableNames.MacAddress, ConditionOperator.Equals, "00:15:5D"),
                    new TestCondition(MachineVariableNames.MacAddress, ConditionOperator.StartsWith, "00:15:5G"),
                    new TestCondition(MachineVariableNames.IPv4Address, ConditionOperator.InSubnet, "10.0.0.0/33"),
                    new TestCondition(MachineVariableNames.MemoryMegabytes, ConditionOperator.GreaterOrEqual, "lots"),
                    new TestCondition(MachineVariableNames.TpmPresent, ConditionOperator.Equals, "maybe"),
                    new TestCondition(MachineVariableNames.DefaultGateway, ConditionOperator.In, "10.0.0.1; 10.0.0"),
                    new AnyCondition { Parts = [new TestCondition(MachineVariableNames.LastExitCode, ConditionOperator.Equals, "0")] },
                    new TestCondition("", ConditionOperator.Exists),
                    new TestCondition(MachineVariableNames.Model, ConditionOperator.Equals, " "),
                ],
            },
            null,
            [
                new NamedValue("1st", "x"),
                new NamedValue("Model", "x"),
                new NamedValue("ddtSecret", "x"),
                new NamedValue("Office", "Vienna"),
                new NamedValue("office", "Graz"),
                new NamedValue("Tag", "{{SerialNumber|shout}}"),
            ],
            [gone]);

    // A rule is saved with what is wrong with it, as an editor saves while the administrator types, and matches nothing
    // until it is fixed.
    [Fact]
    public async Task SavesARuleWithItsProblems()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        Guid gone = Guid.NewGuid();

        RuleView rule = await administrator.CreatedRuleAsync(EverythingWrong(gone));

        Assert.Equal(
            [
                "when.parts[0].variable",
                "when.parts[1].operator",
                "when.parts[2].value",
                "when.parts[3].value",
                "when.parts[4].value",
                "when.parts[5].value",
                "when.parts[6].value",
                "when.parts[7].value",
                "when.parts[8].parts[0].variable",
                "when.parts[9].variable",
                "when.parts[10].value",
                "values[0].name",
                "values[1].name",
                "values[2].name",
                "values[4].name",
                "values[5].value",
                "roleIds[0]",
            ],
            rule.Problems.Select(p => p.Field));
        Assert.Equal(EverythingWrongCodes, rule.Problems.Select(p => p.Code));
        Assert.All(rule.Problems, problem => Assert.Null(problem.StepId));
        Assert.Equal("This comparison does not fit Model, which holds any text.", rule.Problems[1].Message);

        // Fixed, the rule may test the value another rule sets.
        RuleView fixedRule = await RegisteredMachine.ReadAsync<RuleView>(await administrator.PutAsync(
            $"{RuleRequests.Rules}/{rule.Id}",
            RuleRequests.Save(rule, save => save with
            {
                When = new TestCondition("office", ConditionOperator.Equals, "Graz"),
                Values = [new NamedValue("Office", "Vienna")],
                RoleIds = [],
            })));

        Assert.Empty(fixedRule.Problems);
    }

    [Fact]
    public async Task RefusesARuleThatCannotBeStored()
    {
        Guid sequence = (await application.RunnableSequenceAsync()).Id;
        ConditionNode test = new TestCondition(MachineVariableNames.Model, ConditionOperator.Equals, "x");

        Assert.Equal(["name"], await RefusedFieldsAsync(RuleRequests.Rule(" \0 ", test)));
        Assert.Equal(["name"], await RefusedFieldsAsync(RuleRequests.Rule(new string('n', RuleLimits.MaxNameLength + 1), test)));
        Assert.Equal(["description"], await RefusedFieldsAsync(RuleRequests.Rule("Rule", test) with { Description = new string('d', 1025) }));
        Assert.Equal(["sequenceId"], await RefusedFieldsAsync(RuleRequests.Rule("Rule", test, Guid.NewGuid())));
        Assert.Equal(["when"], await RefusedFieldsAsync(RuleRequests.Rule("Rule", new AnyCondition { Parts = [.. Enumerable.Repeat(test, 21)] })));
        Assert.Equal(
            ["when"],
            await RefusedFieldsAsync(RuleRequests.Rule(
                "Rule",
                new AllCondition { Parts = [new AnyCondition { Parts = [new NoneCondition { Parts = [new AllCondition { Parts = [new AnyCondition { Parts = [test] }] }] }] }] })));
        Assert.Equal(["values"], await RefusedFieldsAsync(RuleRequests.Rule("Rule", test, values: [.. Enumerable.Range(0, 65).Select(i => new NamedValue($"V{i}", "x"))])));
        Assert.Equal(["values"], await RefusedFieldsAsync(RuleRequests.Rule("Rule", test, values: [new NamedValue("Long", new string('v', 1025))])));
        Assert.Equal(["roleIds"], await RefusedFieldsAsync(RuleRequests.Rule("Rule", test, roleIds: [.. Enumerable.Range(0, 33).Select(_ => Guid.NewGuid())])));

        // Four groups deep is as deep as a condition goes.
        await (await application.AdministratorAsync()).CreatedRuleAsync(RuleRequests.Rule(
            "Deep",
            new AllCondition { Parts = [new AnyCondition { Parts = [new NoneCondition { Parts = [new AllCondition { Parts = [test] }] }] }] },
            sequence));
    }

    // A save names the revision it was made on: one over a newer save is refused with the rule as it is now.
    [Fact]
    public async Task ChangesARuleOnlyAtTheRevisionItWasRead()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        SequenceView first = await application.RunnableSequenceAsync();
        SequenceView second = await application.RunnableSequenceAsync();
        RuleView rule = await administrator.CreatedRuleAsync(RuleRequests.MacRule(first.Id, RuleRequests.RandomMac()));
        string path = $"{RuleRequests.Rules}/{rule.Id}";

        RuleView changed = await RegisteredMachine.ReadAsync<RuleView>(await administrator.PutAsync(
            path,
            RuleRequests.Save(rule, save => save with { Name = "Renamed", SequenceId = second.Id, Enabled = false })));

        Assert.Equal((rule.Id, rule.Position, 2L, "Renamed", second.Id, second.Name, false), (changed.Id, changed.Position, changed.Revision, changed.Name, changed.SequenceId, changed.SequenceName, changed.Enabled));
        Assert.Equal(
            $"Rule {rule.Position + 1}, Renamed. Changed the name, turned it off, the sequence.",
            (await AuditAsync(rule.Id))[^1].Detail);

        HttpResponseMessage stale = await administrator.PutAsync(path, RuleRequests.Save(rule, save => save with { Name = "Mine" }));
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        RuleView current = await BodyAsync<RuleView>(stale);
        Assert.Equal((2L, "Renamed"), (current.Revision, current.Name));

        // An autosave of what is stored changes nothing and records nothing.
        int audited = (await AuditAsync(rule.Id)).Count;
        RuleView same = await RegisteredMachine.ReadAsync<RuleView>(await administrator.PutAsync(path, RuleRequests.Save(changed)));
        Assert.Equal(2L, same.Revision);
        Assert.Equal(audited, (await AuditAsync(rule.Id)).Count);

        Assert.Equal(HttpStatusCode.NotFound, (await administrator.PutAsync($"{RuleRequests.Rules}/{Guid.NewGuid()}", RuleRequests.Save(changed))).StatusCode);
    }

    // The order is the whole list, saved in one go; the rules keep their revisions, as only their places changed.
    [Fact]
    public async Task ReordersTheWholeListInOneGo()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        await using LiveListener live = await LiveListener.StartAsync(application, administrator);
        ChannelReader<RuleView[]> pushes = live.Listen<RuleView[]>(LiveEvents.RulesChanged);
        Guid sequence = (await application.RunnableSequenceAsync()).Id;
        RuleView a = await administrator.CreatedRuleAsync(RuleRequests.ModelRule(sequence, RuleRequests.UniqueModel()) with { Name = "A" });
        RuleView b = await administrator.CreatedRuleAsync(RuleRequests.ModelRule(sequence, RuleRequests.UniqueModel()) with { Name = "B" });
        RuleView c = await administrator.CreatedRuleAsync(RuleRequests.ModelRule(sequence, RuleRequests.UniqueModel()) with { Name = "C" });
        List<Guid> others = [.. (await administrator.RulesAsync()).Select(r => r.Id).Except([a.Id, b.Id, c.Id])];

        IReadOnlyList<RuleView> reordered = await RegisteredMachine.ReadAsync<IReadOnlyList<RuleView>>(
            await administrator.ReorderAsync([c.Id, a.Id, .. others, b.Id]));

        Assert.Equal([c.Id, a.Id, .. others, b.Id], reordered.Select(r => r.Id));
        AssertNumbered(reordered);
        Assert.All(reordered.Where(r => r.Id == a.Id || r.Id == b.Id || r.Id == c.Id), r => Assert.Equal(1, r.Revision));
        RuleView[] pushed = await LiveListener.NextAsync(pushes, rules => rules.Length > 0 && rules[0].Id == c.Id);
        Assert.Equal(reordered.Select(r => r.Id), pushed.Select(r => r.Id));
        Assert.StartsWith("Moved C from ", (await AuditAsync(AuditActions.RuleReordered))[^1].Detail, StringComparison.Ordinal);

        // The same order again moves nothing and records nothing.
        int audited = (await AuditAsync(AuditActions.RuleReordered)).Count;
        (await administrator.ReorderAsync([.. reordered.Select(r => r.Id)])).EnsureSuccessStatusCode();
        Assert.Equal(audited, (await AuditAsync(AuditActions.RuleReordered)).Count);

        // An order made before a rule was added or removed never applies.
        HttpResponseMessage missing = await administrator.ReorderAsync([.. reordered.Select(r => r.Id).Where(id => id != a.Id)]);
        Assert.Equal(HttpStatusCode.Conflict, missing.StatusCode);
        Assert.Equal(reordered.Select(r => r.Id), (await BodyAsync<IReadOnlyList<RuleView>>(missing)).Select(r => r.Id));
        Assert.Equal(HttpStatusCode.Conflict, (await administrator.ReorderAsync([.. reordered.Select(r => r.Id), Guid.NewGuid()])).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await administrator.ReorderAsync([.. reordered.Select(r => r.Id), a.Id])).StatusCode);
        Assert.Equal(reordered.Select(r => r.Id), (await administrator.RulesAsync()).Select(r => r.Id));
    }

    [Fact]
    public async Task DeletingARuleMovesTheOnesBelowItUp()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        Guid sequence = (await application.RunnableSequenceAsync()).Id;
        RuleView first = await administrator.CreatedRuleAsync(RuleRequests.ModelRule(sequence, RuleRequests.UniqueModel()));
        RuleView middle = await administrator.CreatedRuleAsync(RuleRequests.ModelRule(sequence, RuleRequests.UniqueModel()) with { Name = "Middle" });
        RuleView last = await administrator.CreatedRuleAsync(RuleRequests.ModelRule(sequence, RuleRequests.UniqueModel()));

        IReadOnlyList<RuleView> left = await RegisteredMachine.ReadAsync<IReadOnlyList<RuleView>>(
            await administrator.DeleteAsync($"{RuleRequests.Rules}/{middle.Id}"));

        AssertNumbered(left);
        Assert.DoesNotContain(left, r => r.Id == middle.Id);
        Assert.Equal(first.Position, left.Single(r => r.Id == first.Id).Position);
        Assert.Equal(last.Position - 1, left.Single(r => r.Id == last.Id).Position);
        Assert.Equal(
            (AuditActions.RuleDeleted, $"Rule {middle.Position + 1}, Middle."),
            ((await AuditAsync(middle.Id))[^1].Action, (await AuditAsync(middle.Id))[^1].Detail));
        Assert.Equal(HttpStatusCode.NotFound, (await administrator.DeleteAsync($"{RuleRequests.Rules}/{middle.Id}")).StatusCode);

        // The next rule goes below the last.
        RuleView next = await administrator.CreatedRuleAsync(RuleRequests.ModelRule(sequence, RuleRequests.UniqueModel()));
        Assert.Equal(left.Count, next.Position);
    }

    [Fact]
    public async Task OnlyAnAdministratorWritesRules()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        using SignedInClient viewer = await application.SignInAsync(DdtRoleNames.Viewer);
        using SignedInClient operatorClient = await application.SignInAsync(DdtRoleNames.Operator);
        SequenceView sequence = await application.RunnableSequenceAsync();
        RuleView rule = await administrator.CreatedRuleAsync(RuleRequests.MacRule(sequence.Id, RuleRequests.RandomMac()));
        SaveRuleRequest other = RuleRequests.MacRule(sequence.Id, RuleRequests.RandomMac());

        Assert.Equal(HttpStatusCode.OK, (await viewer.GetAsync(RuleRequests.Rules)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await operatorClient.PostAsync(RuleRequests.Rules, other)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await operatorClient.PutAsync($"{RuleRequests.Rules}/{rule.Id}", RuleRequests.Save(rule))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await operatorClient.DeleteAsync($"{RuleRequests.Rules}/{rule.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await operatorClient.ReorderAsync([rule.Id])).StatusCode);
    }

    [Fact]
    public async Task ASequenceThatRulesChooseStaysUntilTheyGo()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        SequenceView sequence = await application.RunnableSequenceAsync();
        RuleView first = await administrator.CreatedRuleAsync(RuleRequests.MacRule(sequence.Id, RuleRequests.RandomMac()));
        RuleView second = await administrator.CreatedRuleAsync(RuleRequests.ModelRule(sequence.Id, RuleRequests.UniqueModel()));

        HttpResponseMessage chosen = await administrator.DeleteAsync($"{SequenceRequests.Sequences}/{sequence.Id}");
        Assert.Equal(HttpStatusCode.Conflict, chosen.StatusCode);
        Assert.StartsWith("2 rules choose this sequence.", await TestDatabase.TitleAsync(chosen), StringComparison.Ordinal);

        (await administrator.DeleteAsync($"{RuleRequests.Rules}/{first.Id}")).EnsureSuccessStatusCode();
        Assert.StartsWith(
            "A rule chooses this sequence.",
            await TestDatabase.TitleAsync(await administrator.DeleteAsync($"{SequenceRequests.Sequences}/{sequence.Id}")),
            StringComparison.Ordinal);

        // Letting it choose none is enough.
        (await administrator.PutAsync($"{RuleRequests.Rules}/{second.Id}", RuleRequests.Save(second, save => save with { SequenceId = null }))).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.NoContent, (await administrator.DeleteAsync($"{SequenceRequests.Sequences}/{sequence.Id}")).StatusCode);
    }

    [Fact]
    public async Task PushesEveryRuleWhenTheSequenceOneChoosesIsRenamed()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        await using LiveListener live = await LiveListener.StartAsync(application, administrator);
        ChannelReader<RuleView[]> pushes = live.Listen<RuleView[]>(LiveEvents.RulesChanged);
        SequenceView sequence = await application.RunnableSequenceAsync();
        RuleView rule = await administrator.CreatedRuleAsync(RuleRequests.ModelRule(sequence.Id, RuleRequests.UniqueModel()));

        string renamed = $"Renamed {Guid.NewGuid():N}";
        (await administrator.SaveSequenceAsync(sequence, name: renamed)).EnsureSuccessStatusCode();
        RuleView[] afterRename = await LiveListener.NextAsync(pushes, rules => rules.Any(r => r.Id == rule.Id && r.SequenceName == renamed));

        Assert.Equal((await administrator.RulesAsync()).Select(r => (r.Id, r.SequenceName)), afterRename.Select(r => (r.Id, r.SequenceName)));
    }

    // How many machines a rule matches changes when machines register, so the list goes out again then.
    [Fact]
    public async Task CountsTheMachinesARuleMatchesAgainWhenOneRegisters()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        await using LiveListener live = await LiveListener.StartAsync(application, administrator);
        ChannelReader<RuleView[]> pushes = live.Listen<RuleView[]>(LiveEvents.RulesChanged);
        string model = RuleRequests.UniqueModel();
        RuleView rule = await administrator.CreatedRuleAsync(RuleRequests.ModelRule(null, model));
        Assert.Equal(0, rule.MatchingMachines);

        using RegisteredMachine machine = await application.RegisterModelAsync("Dell Inc.", model);

        await LiveListener.NextAsync(pushes, rules => rules.Any(r => r.Id == rule.Id && r.MatchingMachines == 1));
        Assert.Equal(1, (await administrator.RulesAsync()).Single(r => r.Id == rule.Id).MatchingMachines);
    }
}
