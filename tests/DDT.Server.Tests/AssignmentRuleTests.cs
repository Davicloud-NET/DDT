// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Channels;
using DDT.Contracts.Rules;
using DDT.Contracts.Sequences;
using DDT.Server.Authentication;
using DDT.Server.Data;
using DDT.Server.Live;
using DDT.Server.Machines;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DDT.Server.Tests;

public sealed class AssignmentRuleTests(DdtApplication application) : IClassFixture<DdtApplication>
{
    private static Task<T> ReadAsync<T>(HttpResponseMessage response) => RegisteredMachine.ReadAsync<T>(response);

    private Task<List<AuditEvent>> AuditAsync(Guid ruleId)
    {
        string subject = ruleId.ToString("D");

        return application.QueryAsync(database => database.AuditEvents
            .AsNoTracking()
            .Where(e => e.SubjectId == subject)
            .OrderBy(e => e.Id)
            .ToListAsync(TestContext.Current.CancellationToken));
    }

    private static string WithSeparators(string mac, char separator) => string.Join(separator, mac.Chunk(2).Select(pair => new string(pair)));

    private async Task<HttpResponseMessage> PostAsync(SaveAssignmentRuleRequest rule) =>
        await (await application.AdministratorAsync()).PostAsync(RuleRequests.Rules, rule);

    private async Task<HttpResponseMessage> PutAsync(Guid ruleId, SaveAssignmentRuleRequest rule) =>
        await (await application.AdministratorAsync()).PutAsync($"{RuleRequests.Rules}/{ruleId}", rule);

    private async Task<IEnumerable<string>> ProblemKeysAsync(SaveAssignmentRuleRequest rule)
    {
        HttpResponseMessage response = await PostAsync(rule);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        return (await response.Content.ReadFromJsonAsync<ValidationProblemDetails>(TestContext.Current.CancellationToken))!.Errors.Keys;
    }

    [Fact]
    public async Task CreatesRulesByMacAddressAndByModel()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        using SignedInClient viewer = await application.SignInAsync(DdtRoleNames.Viewer);
        await using LiveListener live = await LiveListener.StartAsync(application, viewer);
        ChannelReader<AssignmentRuleView[]> changes = live.Listen<AssignmentRuleView[]>(LiveEvents.RulesChanged);
        SequenceView sequence = await application.RunnableSequenceAsync();
        string mac = RuleRequests.RandomMac();
        string model = RuleRequests.UniqueModel();

        HttpResponseMessage created = await PostAsync(
            new SaveAssignmentRuleRequest(AssignmentRuleKind.Mac, WithSeparators(mac, '-').ToLowerInvariant(), "x", "y", sequence.Id, " Lab\0 "));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        AssignmentRuleView byMac = await ReadAsync<AssignmentRuleView>(created);

        Assert.Equal($"/api/rules/{byMac.Id}", created.Headers.Location?.OriginalString);
        Assert.Equal(
            new AssignmentRuleView(byMac.Id, AssignmentRuleKind.Mac, mac, null, null, sequence.Id, sequence.Name, "Lab", byMac.UpdatedUtc, byMac.UpdatedBy),
            byMac);
        Assert.Contains(byMac, await LiveListener.NextAsync(changes, rules => rules.Any(r => r.Id == byMac.Id)));

        AssignmentRuleView byModel = await administrator.CreatedRuleAsync(
            RuleRequests.ModelRule(sequence.Id, $"  {model}  7* ", " Dell   Inc. "));

        Assert.Equal("Dell Inc.", byModel.Manufacturer);
        Assert.Equal($"{model} 7*", byModel.Model);
        Assert.Null(byModel.Mac);

        IReadOnlyList<AssignmentRuleView> listed = await ReadAsync<IReadOnlyList<AssignmentRuleView>>(await viewer.GetAsync(RuleRequests.Rules));
        Assert.Contains(byMac, listed);
        Assert.Contains(byModel, listed);

        // Every rule, in the order the list has them.
        Assert.Equal(listed, await LiveListener.NextAsync(changes, rules => rules.Any(r => r.Id == byModel.Id)));

        AuditEvent audit = Assert.Single(await AuditAsync(byModel.Id));
        Assert.Equal(AuditActions.RuleCreated, audit.Action);
        Assert.Equal($"The rule for model Dell Inc. {model} 7* chooses {sequence.Name}.", audit.Detail);
        Assert.Equal(
            $"The rule for MAC address {WithSeparators(mac, ':')} chooses {sequence.Name}.",
            Assert.Single(await AuditAsync(byMac.Id)).Detail);
    }

    [Fact]
    public async Task RefusesASecondRuleForTheSameMachinesAndNamesTheFirst()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        SequenceView sequence = await application.RunnableSequenceAsync();
        string mac = RuleRequests.RandomMac();
        string model = RuleRequests.UniqueModel();
        AssignmentRuleView byMac = await administrator.CreatedRuleAsync(RuleRequests.MacRule(sequence.Id, mac));
        AssignmentRuleView byModel = await administrator.CreatedRuleAsync(RuleRequests.ModelRule(sequence.Id, model, "Dell Inc."));
        AssignmentRuleView anyMaker = await administrator.CreatedRuleAsync(RuleRequests.ModelRule(sequence.Id, model));

        HttpResponseMessage sameMac = await PostAsync(RuleRequests.MacRule(sequence.Id, WithSeparators(mac, ':')));
        Assert.Equal(HttpStatusCode.Conflict, sameMac.StatusCode);
        ProblemDetails problem = (await sameMac.Content.ReadFromJsonAsync<ProblemDetails>(TestContext.Current.CancellationToken))!;
        Assert.StartsWith("There is a rule for MAC address ", problem.Title, StringComparison.Ordinal);
        Assert.Equal(byMac.Id.ToString(), ((JsonElement)problem.Extensions["ruleId"]!).GetString());

        HttpResponseMessage sameModel = await PostAsync(RuleRequests.ModelRule(sequence.Id, model.ToUpperInvariant(), " dell inc. "));
        Assert.Equal(HttpStatusCode.Conflict, sameModel.StatusCode);

        // Turning the rule for any maker into the one for Dell would make two of them.
        Assert.Equal(HttpStatusCode.Conflict, (await PutAsync(anyMaker.Id, RuleRequests.ModelRule(sequence.Id, model, "Dell Inc."))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await PutAsync(byModel.Id, RuleRequests.ModelRule(sequence.Id, model, "DELL INC."))).StatusCode);
    }

    [Fact]
    public async Task RefusesARuleThatCannotMatchAMachine()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        Guid sequence = (await application.RunnableSequenceAsync()).Id;
        string model = RuleRequests.UniqueModel();

        Assert.Equal(["mac"], await ProblemKeysAsync(RuleRequests.MacRule(sequence, "00:15:5D")));
        Assert.Equal(["mac"], await ProblemKeysAsync(RuleRequests.MacRule(sequence, "00155D01020G")));
        Assert.Equal(["model"], await ProblemKeysAsync(RuleRequests.ModelRule(sequence, " ")));
        Assert.Equal(["model"], await ProblemKeysAsync(RuleRequests.ModelRule(sequence, "System Product Name")));
        Assert.Equal(["model"], await ProblemKeysAsync(RuleRequests.ModelRule(sequence, "20*")));
        Assert.Equal(["model"], await ProblemKeysAsync(RuleRequests.ModelRule(sequence, "Lat*tude")));
        Assert.Equal(["manufacturer"], await ProblemKeysAsync(RuleRequests.ModelRule(sequence, model, "Dell*")));
        Assert.Equal(["manufacturer"], await ProblemKeysAsync(RuleRequests.ModelRule(sequence, model, "To be filled by O.E.M.")));
        Assert.Equal(["sequenceId"], await ProblemKeysAsync(RuleRequests.MacRule(Guid.NewGuid(), RuleRequests.RandomMac())));
        Assert.Equal(
            ["description"],
            await ProblemKeysAsync(RuleRequests.MacRule(sequence, RuleRequests.RandomMac()) with { Description = new string('d', 257) }));

        string unknownKind = $$"""{"kind":"Serial","sequenceId":"{{sequence}}"}""";
        Assert.Equal(HttpStatusCode.BadRequest, (await administrator.SendJsonAsync(HttpMethod.Post, RuleRequests.Rules, unknownKind)).StatusCode);
    }

    [Fact]
    public async Task ChangesAndDeletesARule()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        SequenceView first = await application.RunnableSequenceAsync();
        SequenceView second = await application.RunnableSequenceAsync();
        AssignmentRuleView rule = await administrator.CreatedRuleAsync(RuleRequests.MacRule(first.Id, RuleRequests.RandomMac()));

        AssignmentRuleView changed = await ReadAsync<AssignmentRuleView>(await PutAsync(rule.Id, RuleRequests.ModelRule(second.Id, RuleRequests.UniqueModel())));

        Assert.Equal(rule.Id, changed.Id);
        Assert.Equal(AssignmentRuleKind.Model, changed.Kind);
        Assert.Null(changed.Mac);
        Assert.Equal(second.Name, changed.SequenceName);
        Assert.Equal(AuditActions.RuleChanged, (await AuditAsync(rule.Id))[^1].Action);

        Assert.Equal(HttpStatusCode.NoContent, (await administrator.DeleteAsync($"{RuleRequests.Rules}/{rule.Id}")).StatusCode);
        Assert.Equal(AuditActions.RuleDeleted, (await AuditAsync(rule.Id))[^1].Action);
        IReadOnlyList<AssignmentRuleView> listed = await ReadAsync<IReadOnlyList<AssignmentRuleView>>(await administrator.GetAsync(RuleRequests.Rules));
        Assert.DoesNotContain(listed, r => r.Id == rule.Id);
        Assert.Equal(HttpStatusCode.NotFound, (await administrator.DeleteAsync($"{RuleRequests.Rules}/{rule.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await PutAsync(rule.Id, RuleRequests.MacRule(first.Id, RuleRequests.RandomMac()))).StatusCode);
    }

    [Fact]
    public async Task OnlyAnAdministratorWritesRules()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        using SignedInClient viewer = await application.SignInAsync(DdtRoleNames.Viewer);
        using SignedInClient operatorClient = await application.SignInAsync(DdtRoleNames.Operator);
        SequenceView sequence = await application.RunnableSequenceAsync();
        AssignmentRuleView rule = await administrator.CreatedRuleAsync(RuleRequests.MacRule(sequence.Id, RuleRequests.RandomMac()));

        SaveAssignmentRuleRequest other = RuleRequests.MacRule(sequence.Id, RuleRequests.RandomMac());

        Assert.Equal(HttpStatusCode.OK, (await viewer.GetAsync(RuleRequests.Rules)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await operatorClient.PostAsync(RuleRequests.Rules, other)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await operatorClient.PutAsync($"{RuleRequests.Rules}/{rule.Id}", other)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await operatorClient.DeleteAsync($"{RuleRequests.Rules}/{rule.Id}")).StatusCode);
    }

    [Fact]
    public async Task ASequenceThatRulesChooseStaysUntilTheyGo()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        SequenceView sequence = await application.RunnableSequenceAsync();
        AssignmentRuleView first = await administrator.CreatedRuleAsync(RuleRequests.MacRule(sequence.Id, RuleRequests.RandomMac()));
        AssignmentRuleView second = await administrator.CreatedRuleAsync(RuleRequests.ModelRule(sequence.Id, RuleRequests.UniqueModel()));

        HttpResponseMessage chosen = await administrator.DeleteAsync($"{SequenceRequests.Sequences}/{sequence.Id}");
        Assert.Equal(HttpStatusCode.Conflict, chosen.StatusCode);
        Assert.StartsWith("2 rules choose this sequence.", await TestDatabase.TitleAsync(chosen), StringComparison.Ordinal);

        (await administrator.DeleteAsync($"{RuleRequests.Rules}/{first.Id}")).EnsureSuccessStatusCode();
        Assert.StartsWith(
            "A rule chooses this sequence.",
            await TestDatabase.TitleAsync(await administrator.DeleteAsync($"{SequenceRequests.Sequences}/{sequence.Id}")),
            StringComparison.Ordinal);

        (await administrator.DeleteAsync($"{RuleRequests.Rules}/{second.Id}")).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.NoContent, (await administrator.DeleteAsync($"{SequenceRequests.Sequences}/{sequence.Id}")).StatusCode);
    }

    // Rules are few and a change can move one among the others, so every push carries them all, in the list's order.
    [Fact]
    public async Task PushesEveryRuleWhenOneChangesOrItsSequenceIsRenamed()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        await using LiveListener live = await LiveListener.StartAsync(application, administrator);
        ChannelReader<AssignmentRuleView[]> pushes = live.Listen<AssignmentRuleView[]>(LiveEvents.RulesChanged);
        SequenceView sequence = await application.RunnableSequenceAsync();
        AssignmentRuleView rule = await administrator.CreatedRuleAsync(RuleRequests.ModelRule(sequence.Id, RuleRequests.UniqueModel()));
        await LiveListener.NextAsync(pushes, rules => rules.Any(r => r.Id == rule.Id));

        AssignmentRuleView changed = await ReadAsync<AssignmentRuleView>(await PutAsync(
            rule.Id,
            RuleRequests.ModelRule(sequence.Id, rule.Model!) with { Description = "Changed" }));
        Assert.Contains(changed, await LiveListener.NextAsync(pushes, rules => rules.Any(r => r.Id == rule.Id && r.Description == "Changed")));

        string renamed = $"Renamed {Guid.NewGuid():N}";
        (await administrator.SaveSequenceAsync(sequence, name: renamed)).EnsureSuccessStatusCode();
        AssignmentRuleView[] afterRename = await LiveListener.NextAsync(pushes, rules => rules.Any(r => r.Id == rule.Id && r.SequenceName == renamed));
        Assert.Equal(await ReadAsync<IReadOnlyList<AssignmentRuleView>>(await administrator.GetAsync(RuleRequests.Rules)), afterRename);

        Assert.Equal(HttpStatusCode.NoContent, (await administrator.DeleteAsync($"{RuleRequests.Rules}/{rule.Id}")).StatusCode);
        Assert.DoesNotContain(await LiveListener.NextAsync(pushes), r => r.Id == rule.Id);
    }
}
