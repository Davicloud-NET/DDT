// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Agents;
using DDT.Contracts.Machines;
using DDT.Contracts.Settings;
using DDT.Server.Data;
using DDT.Server.Machines;
using DDT.Server.Records;
using DDT.Server.Settings;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DDT.Server.Tests.Autopilot;

// Sets up the Autopilot settings and keeps hashes as a run's step would hand them over.
internal static class AutopilotLab
{
    public const string TenantId = "contoso.onmicrosoft.com";
    public const string ClientId = "00001111-aaaa-2222-bbbb-3333cccc4444";
    public const string Secret = "client secret 1";

    public static readonly string Hash = Convert.ToBase64String(new byte[3000]);

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    public static async Task<SettingsSectionView<AutopilotSettings>> SetUpAsync(DdtApplication application, string defaultGroupTag = "")
    {
        SignedInClient administrator = await application.AdministratorAsync();

        return await administrator.SavedAsync<AutopilotSettings>(
            SettingsSectionNames.Autopilot,
            values => values with { TenantId = TenantId, ClientId = ClientId, DefaultGroupTag = defaultGroupTag },
            new(Secrets: new Dictionary<string, SecretUpdate> { ["clientSecret"] = new(SecretAction.Set, Secret) }));
    }

    public static async Task TakeDownAsync(DdtApplication application)
    {
        SignedInClient administrator = await application.AdministratorAsync();
        await administrator.SavedAsync<AutopilotSettings>(
            SettingsSectionNames.Autopilot,
            values => values with { TenantId = string.Empty, ClientId = string.Empty, DefaultGroupTag = string.Empty },
            new(Secrets: new Dictionary<string, SecretUpdate> { ["clientSecret"] = new(SecretAction.Clear, null) }));
    }

    // A registered machine with a hash, kept as HardwareHashRecords keeps one from a run.
    public static async Task<Guid> MachineWithHashAsync(DdtApplication application, string serialNumber = "SN-1", string? groupTag = null)
    {
        Guid machineId = (await application.RegisterMachineAsync()).Id;
        using IServiceScope scope = application.Services.CreateScope();
        DdtDbContext database = scope.ServiceProvider.GetRequiredService<DdtDbContext>();
        Machine machine = await database.Machines.SingleAsync(m => m.Id == machineId, Cancellation);
        RecordRefusal? refusal = await scope.ServiceProvider.GetRequiredService<HardwareHashRecords>()
            .SaveAsync(machine, new AgentHardwareHash(serialNumber, Hash, groupTag), Cancellation);

        Assert.Null(refusal);

        return machineId;
    }

    public static Task<MachineHardwareHash> RowAsync(DdtApplication application, Guid machineId) =>
        application.QueryAsync(database => database.MachineHardwareHashes.AsNoTracking().SingleAsync(h => h.MachineId == machineId, Cancellation));

    // Leaves nothing Pending from an earlier test of the class, so a pass sees only this test's rows.
    public static Task SettleEarlierRowsAsync(DdtApplication application)
    {
        application.Autopilot.Reset();

        return application.QueryAsync(database => database.MachineHardwareHashes
            .Where(h => h.UploadState == HardwareHashUploadState.Pending)
            .ExecuteUpdateAsync(set => set.SetProperty(h => h.UploadState, HardwareHashUploadState.None), Cancellation));
    }
}
