// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DDT.Contracts.Machines;
using DDT.Server.Authentication;
using DDT.Server.Machines;
using DDT.Server.Records;
using Microsoft.EntityFrameworkCore;
using Xunit;
using static DDT.Server.Tests.RecordingRuns;

namespace DDT.Server.Tests;

// A machine's recovery keys on the web: everyone sees they exist, only administrators reveal or delete one, and every
// reveal and deletion is audited without the password.
public sealed class MachineRecordTests(DdtApplication application) : IClassFixture<DdtApplication>
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private async Task<Guid> MachineAsync() => (await application.RegisterMachineAsync()).Id;

    private Task<List<(string Action, string? Detail)>> AuditAsync(Guid machineId)
    {
        string subject = machineId.ToString("D");

        return application.QueryAsync(async database => (await database.AuditEvents
                .Where(e => e.SubjectId == subject && (e.Action.StartsWith("recovery-key.") || e.Action == AuditActions.MachineRemoved))
                .OrderBy(e => e.Id)
                .Select(e => new { e.Action, e.Detail })
                .ToListAsync(Cancellation))
            .Select(e => (e.Action, e.Detail))
            .ToList());
    }

    [Fact]
    public async Task EveryoneSeesTheKeysWithoutTheirPasswords()
    {
        Guid machineId = await MachineAsync();
        RecoveryKey key = await SeedKeyAsync(application, machineId);
        using SignedInClient viewer = await application.SignInAsync(DdtRoleNames.Viewer);

        HttpResponseMessage response = await viewer.GetAsync($"/api/machines/{machineId}/records");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        string json = await response.Content.ReadAsStringAsync(Cancellation);
        Assert.DoesNotContain(Password, json, StringComparison.Ordinal);
        MachineRecordsView view = JsonSerializer.Deserialize<MachineRecordsView>(json, TestJson.Options)!;
        RecoveryKeyView shown = Assert.Single(view.RecoveryKeys);
        Assert.Equal((key.Id, key.ProtectorId, "C:"), (shown.Id, shown.ProtectorId, shown.Volume));
        Assert.Null(view.HardwareHash);
        Assert.Equal(HttpStatusCode.NotFound, (await viewer.GetAsync($"/api/machines/{Guid.NewGuid()}/records")).StatusCode);
    }

    [Fact]
    public async Task AnAdministratorRevealsAKeyAndTheRevealIsAudited()
    {
        Guid machineId = await MachineAsync();
        RecoveryKey key = await SeedKeyAsync(application, machineId);
        SignedInClient administrator = await application.AdministratorAsync();

        HttpResponseMessage response = await administrator.PostAsync($"/api/machines/{machineId}/recovery-keys/{key.Id}/reveal");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        Assert.Equal(Password, (await response.Content.ReadFromJsonAsync<RecoveryKeyReveal>(TestJson.Options, Cancellation))!.Password);
        Assert.Equal([(AuditActions.RecoveryKeyRevealed, $"Key ID {key.ProtectorId.ToString("D").ToUpperInvariant()} of C:.")], await AuditAsync(machineId));
    }

    [Theory]
    [InlineData(DdtRoleNames.Operator)]
    [InlineData(DdtRoleNames.Viewer)]
    public async Task OnlyAnAdministratorRevealsOrDeletes(string role)
    {
        Guid machineId = await MachineAsync();
        RecoveryKey key = await SeedKeyAsync(application, machineId, replacedUtc: DateTimeOffset.UtcNow);
        using SignedInClient client = await application.SignInAsync(role);

        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsync($"/api/machines/{machineId}/recovery-keys/{key.Id}/reveal")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.DeleteAsync($"/api/machines/{machineId}/recovery-keys/{key.Id}")).StatusCode);
        Assert.Empty(await AuditAsync(machineId));
    }

    [Fact]
    public async Task AKeyOfAnotherMachineIsNotFound()
    {
        Guid machineId = await MachineAsync();
        RecoveryKey key = await SeedKeyAsync(application, await MachineAsync());
        SignedInClient administrator = await application.AdministratorAsync();

        Assert.Equal(HttpStatusCode.NotFound, (await administrator.PostAsync($"/api/machines/{machineId}/recovery-keys/{key.Id}/reveal")).StatusCode);
    }

    [Fact]
    public async Task OnlyAReplacedKeyCanBeDeleted()
    {
        Guid machineId = await MachineAsync();
        RecoveryKey current = await SeedKeyAsync(application, machineId);
        RecoveryKey replaced = await SeedKeyAsync(application, machineId, replacedUtc: DateTimeOffset.UtcNow);
        SignedInClient administrator = await application.AdministratorAsync();

        HttpResponseMessage refused = await administrator.DeleteAsync($"/api/machines/{machineId}/recovery-keys/{current.Id}");
        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        Assert.Equal("recoveryKey.current", await AccountRequests.CodeAsync(refused));

        Assert.Equal(HttpStatusCode.NoContent, (await administrator.DeleteAsync($"/api/machines/{machineId}/recovery-keys/{replaced.Id}")).StatusCode);
        Assert.Equal([current.Id], (await KeysAsync(application, machineId)).Select(k => k.Id));
        Assert.Equal([(AuditActions.RecoveryKeyDeleted, $"Key ID {replaced.ProtectorId.ToString("D").ToUpperInvariant()} of C:.")], await AuditAsync(machineId));
    }

    [Fact]
    public async Task TheSearchFindsAMachineByItsKeyIdOrItsStart()
    {
        Guid machineId = await MachineAsync();
        RecoveryKey key = await SeedKeyAsync(application, machineId);
        using SignedInClient viewer = await application.SignInAsync(DdtRoleNames.Viewer);
        string digits = key.ProtectorId.ToString("N");

        Assert.Equal([machineId], await FoundAsync(viewer, key.ProtectorId.ToString("B").ToUpperInvariant()));
        Assert.Equal([machineId], await FoundAsync(viewer, digits[..8]));
        Assert.Equal([machineId], await FoundAsync(viewer, key.ProtectorId.ToString("D")[..13]));
        Assert.Empty(await FoundAsync(viewer, digits[..7]));
        Assert.Empty(await FoundAsync(viewer, "zzzzzzzz"));
    }

    [Fact]
    public async Task AMachineWithKeysIsRemovedOnlyOnceTheirCountIsConfirmed()
    {
        Guid machineId = await MachineAsync();
        await SeedKeyAsync(application, machineId, replacedUtc: DateTimeOffset.UtcNow);
        await SeedKeyAsync(application, machineId);
        SignedInClient administrator = await application.AdministratorAsync();
        (await administrator.PostAsync($"/api/machines/{machineId}/reject")).EnsureSuccessStatusCode();

        HttpResponseMessage asked = await administrator.DeleteAsync($"/api/machines/{machineId}");
        Assert.Equal(HttpStatusCode.Conflict, asked.StatusCode);
        Assert.Equal("machine.removeRecoveryKeys", await AccountRequests.CodeAsync(asked));
        Assert.Equal(HttpStatusCode.Conflict, (await administrator.DeleteAsync($"/api/machines/{machineId}?recoveryKeys=1")).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await administrator.DeleteAsync($"/api/machines/{machineId}?recoveryKeys=2")).StatusCode);

        Assert.Empty(await KeysAsync(application, machineId));
        (string action, string? detail) = Assert.Single(await AuditAsync(machineId));
        Assert.Equal(AuditActions.MachineRemoved, action);
        Assert.EndsWith("Its 2 recovery keys were deleted with it.", detail, StringComparison.Ordinal);
    }

    private static async Task<IReadOnlyList<Guid>> FoundAsync(SignedInClient client, string keyId) =>
        (await client.Http.GetFromJsonAsync<IReadOnlyList<Guid>>(
            new Uri($"/api/machines/recovery-keys?keyId={Uri.EscapeDataString(keyId)}", UriKind.Relative),
            TestJson.Options,
            Cancellation))!;
}
