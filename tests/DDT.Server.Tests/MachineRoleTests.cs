// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Net;
using System.Net.Http.Json;
using System.Threading.Channels;
using DDT.Contracts.Rules;
using DDT.Contracts.Sequences;
using DDT.Contracts.Values;
using DDT.Server.Authentication;
using DDT.Server.Data;
using DDT.Server.Live;
using DDT.Server.Machines;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DDT.Server.Tests;

// Machine roles are sets of values that rules give machines. They aren't the roles of users.
public sealed class MachineRoleTests(DdtApplication application) : IClassFixture<DdtApplication>
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private static string Unique(string name) => $"{name} {Guid.NewGuid():N}";

    private Task<List<AuditEvent>> AuditAsync(Guid subject)
    {
        string id = subject.ToString("D");

        return application.QueryAsync(database => database.AuditEvents.AsNoTracking().Where(e => e.SubjectId == id).OrderBy(e => e.Id).ToListAsync(Cancellation));
    }

    private async Task<Dictionary<string, string[]>> RefusedAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        return (await response.Content.ReadFromJsonAsync<ValidationProblemDetails>(TestJson.Options, Cancellation))!.Errors
            .ToDictionary(error => error.Key, error => error.Value);
    }

    [Fact]
    public async Task CreatesChangesAndDeletesAMachineRole()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        using SignedInClient viewer = await application.SignInAsync(DdtRoleNames.Viewer);
        await using LiveListener live = await LiveListener.StartAsync(application, viewer);
        ChannelReader<MachineRoleView[]> pushes = live.Listen<MachineRoleView[]>(LiveEvents.RolesChanged);
        string name = Unique("Kiosk");

        HttpResponseMessage response = await administrator.PostAsync(
            RuleRequests.Roles,
            new SaveMachineRoleRequest(7, $" {name}\0 ", " Locked down ", [new NamedValue(" Wallpaper ", "kiosk.png")]));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        MachineRoleView role = (await response.Content.ReadFromJsonAsync<MachineRoleView>(TestJson.Options, Cancellation))!;

        Assert.Equal($"/api/machine-roles/{role.Id}", response.Headers.Location?.OriginalString);
        Assert.Equal((name, "Locked down", 1L, 0), (role.Name, role.Description, role.Revision, role.RuleCount));
        Assert.Equal([new NamedValue("Wallpaper", "kiosk.png")], role.Values);
        Assert.Contains(await RegisteredMachine.ReadAsync<IReadOnlyList<MachineRoleView>>(await viewer.GetAsync(RuleRequests.Roles)), r => r.Id == role.Id);
        await LiveListener.NextAsync(pushes, roles => roles.Any(r => r.Id == role.Id));

        string path = $"{RuleRequests.Roles}/{role.Id}";
        MachineRoleView changed = await RegisteredMachine.ReadAsync<MachineRoleView>(await administrator.PutAsync(
            path,
            new SaveMachineRoleRequest(1, name, null, [new NamedValue("Wallpaper", "kiosk.png"), new NamedValue("Office", "{{Site|upper}}")])));

        Assert.Equal((2L, (string?)null), (changed.Revision, changed.Description));
        Assert.Equal(2, changed.Values.Count);
        await LiveListener.NextAsync(pushes, roles => roles.Any(r => r.Id == role.Id && r.Revision == 2));

        // A save over a newer version is refused, and the answer has the role as it is now.
        HttpResponseMessage stale = await administrator.PutAsync(path, new SaveMachineRoleRequest(1, "Mine", null, []));
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        Assert.Equal(2L, (await stale.Content.ReadFromJsonAsync<MachineRoleView>(TestJson.Options, Cancellation))!.Revision);

        Assert.Equal(HttpStatusCode.NoContent, (await administrator.DeleteAsync(path)).StatusCode);
        Assert.DoesNotContain(await LiveListener.NextAsync(pushes, roles => roles.All(r => r.Id != role.Id)), r => r.Id == role.Id);
        Assert.Equal(HttpStatusCode.NotFound, (await administrator.DeleteAsync(path)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await administrator.PutAsync(path, new SaveMachineRoleRequest(2, name, null, []))).StatusCode);

        Assert.Equal(
            [
                (AuditActions.RoleCreated, $"{name}."),
                (AuditActions.RoleChanged, $"{name}. Changed the description, the values."),
                (AuditActions.RoleDeleted, $"{name}."),
            ],
            (await AuditAsync(role.Id)).Select(e => (e.Action, e.Detail)));
    }

    // A role has no problems list to save them in, unlike a rule. So anything a run couldn't use is refused.
    [Fact]
    public async Task RefusesARoleWhoseNameIsTakenOrWhoseValuesCannotBeUsed()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        MachineRoleView taken = await administrator.CreatedRoleAsync(new SaveMachineRoleRequest(0, Unique("Finance"), null, []));

        Assert.Equal(
            ["name"],
            (await RefusedAsync(await administrator.PostAsync(RuleRequests.Roles, new SaveMachineRoleRequest(0, taken.Name.ToUpperInvariant(), null, [])))).Keys);
        Assert.Equal(["name"], (await RefusedAsync(await administrator.PostAsync(RuleRequests.Roles, new SaveMachineRoleRequest(0, " ", null, [])))).Keys);
        Assert.Equal(
            ["values[0].name", "values[1].name", "values[2].value"],
            (await RefusedAsync(await administrator.PostAsync(
                RuleRequests.Roles,
                new SaveMachineRoleRequest(
                    0,
                    Unique("Broken"),
                    null,
                    [new NamedValue("SerialNumber", "x"), new NamedValue("with space", "x"), new NamedValue("Tag", "{{Model|left}}")])))).Keys);
        Assert.Equal(
            ["values"],
            (await RefusedAsync(await administrator.PostAsync(
                RuleRequests.Roles,
                new SaveMachineRoleRequest(0, Unique("Many"), null, [.. Enumerable.Range(0, 65).Select(i => new NamedValue($"V{i}", "x"))])))).Keys);

        // Renaming a role to its own name in another case is no clash.
        MachineRoleView renamed = await RegisteredMachine.ReadAsync<MachineRoleView>(await administrator.PutAsync(
            $"{RuleRequests.Roles}/{taken.Id}",
            new SaveMachineRoleRequest(taken.Revision, taken.Name.ToUpperInvariant(), null, [])));
        Assert.Equal(taken.Name.ToUpperInvariant(), renamed.Name);
    }

    // Like a sequence that rules choose, a role stays while rules give it. So no rule ever gives a role that's gone.
    [Fact]
    public async Task ARoleThatRulesGiveStaysUntilTheyNoLongerGiveIt()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        await using LiveListener live = await LiveListener.StartAsync(application, administrator);
        ChannelReader<MachineRoleView[]> pushes = live.Listen<MachineRoleView[]>(LiveEvents.RolesChanged);
        MachineRoleView role = await administrator.CreatedRoleAsync(new SaveMachineRoleRequest(0, Unique("Lab"), null, [new NamedValue("Office", "Lab")]));
        RuleView rule = await administrator.CreatedRuleAsync(RuleRequests.ModelRule(null, RuleRequests.UniqueModel()) with { RoleIds = [role.Id] });

        Assert.Equal(1, (await LiveListener.NextAsync(pushes, roles => roles.Any(r => r.Id == role.Id && r.RuleCount == 1))).Single(r => r.Id == role.Id).RuleCount);

        HttpResponseMessage refused = await administrator.DeleteAsync($"{RuleRequests.Roles}/{role.Id}");
        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        Assert.Equal("A rule gives this machine role. Take it out of the rule, then delete the role.", await TestDatabase.TitleAsync(refused));

        // A rule may test what its machine roles set.
        RuleView testing = await administrator.CreatedRuleAsync(RuleRequests.Rule("Office", new TestCondition("Office", ConditionOperator.Equals, "Lab")));
        Assert.Empty(testing.Problems);

        (await administrator.PutAsync($"{RuleRequests.Rules}/{rule.Id}", RuleRequests.Save(rule, save => save with { RoleIds = [] }))).EnsureSuccessStatusCode();
        await LiveListener.NextAsync(pushes, roles => roles.Any(r => r.Id == role.Id && r.RuleCount == 0));
        Assert.Equal(HttpStatusCode.NoContent, (await administrator.DeleteAsync($"{RuleRequests.Roles}/{role.Id}")).StatusCode);

        // With the role gone, nothing sets what the other rule tests.
        Assert.Equal("rule.conditionUnknownName", Assert.Single((await administrator.RulesAsync()).Single(r => r.Id == testing.Id).Problems).Code);
    }

    [Fact]
    public async Task OnlyAnAdministratorWritesMachineRoles()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        using SignedInClient viewer = await application.SignInAsync(DdtRoleNames.Viewer);
        using SignedInClient operatorClient = await application.SignInAsync(DdtRoleNames.Operator);
        MachineRoleView role = await administrator.CreatedRoleAsync(new SaveMachineRoleRequest(0, Unique("Kiosk"), null, []));
        SaveMachineRoleRequest other = new(0, Unique("Other"), null, []);

        Assert.Equal(HttpStatusCode.OK, (await viewer.GetAsync(RuleRequests.Roles)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await operatorClient.PostAsync(RuleRequests.Roles, other)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await operatorClient.PutAsync($"{RuleRequests.Roles}/{role.Id}", other)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await operatorClient.DeleteAsync($"{RuleRequests.Roles}/{role.Id}")).StatusCode);
    }
}
