// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Net;
using DDT.Contracts.Settings;
using DDT.Pxe;
using DDT.Server.Data;
using DDT.Server.Machines;
using DDT.Server.Settings;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DDT.Server.Tests;

// The pxe section as the page shows it: how each host that serves netboot applied it, the interfaces each found, and a
// rescan that makes every host apply it again.
public sealed class SettingsPxeTests(DdtApplication application) : IClassFixture<DdtApplication>
{
    [Fact]
    public async Task EachHostSaysHowItAppliedTheSectionAndWhichInterfacesItFound()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        SettingsSectionView<PxeSettings> saved = await administrator.SavedAsync<PxeSettings>(
            SettingsSectionNames.Pxe,
            values => values with { Interfaces = ["lab", "gone"], TftpMaxWindowSize = 8 });

        NetworkInterfaceMap interfaces = new("lab, gone", [new ServedInterface(7, "lab", IPAddress.Parse("10.40.0.1")), new ServedInterface(8, "office", IPAddress.Parse("10.50.0.1"))]);
        await PxeSettingsSource.Create(application.Services).Applied(new PxeApplyResult(saved.Version, true, null, interfaces));

        SettingsSectionView<PxeSettings> view = await administrator.SectionAsync<PxeSettings>(SettingsSectionNames.Pxe);
        SettingApplyState state = Assert.Single(view.Apply!);
        Assert.Equal(SettingsHostStates.Host, state.Host);
        Assert.Equal(SettingApplyStatus.Applied, state.State);
        Assert.Equal(saved.Version, state.Version);
        SettingMessage warning = Assert.Single(view.Warnings);
        Assert.Equal("interfaces", warning.Field);
        Assert.Equal(SettingWarningCodes.PxeInterfaceNotFound, warning.Code);
        Assert.Contains("'gone'", warning.Message, StringComparison.Ordinal);

        PxeHostInterfaces host = Assert.Single(await RegisteredMachine.ReadAsync<List<PxeHostInterfaces>>(await administrator.GetAsync("/api/settings/pxe/interfaces")));
        Assert.Equal(["lab", "office"], host.Interfaces.Select(candidate => candidate.Name));
        Assert.Equal([true, false], host.Interfaces.Select(candidate => candidate.Served));
        Assert.Equal(["10.40.0.1"], host.Interfaces[0].Addresses);
        Assert.Equal(["gone"], host.Unmatched);

        // A host that has not applied the newest version yet is pending.
        SettingsSectionView<PxeSettings> rescanned = await RegisteredMachine.ReadAsync<SettingsSectionView<PxeSettings>>(
            await administrator.PostAsync("/api/settings/pxe/rescan"));
        Assert.Equal(saved.Version + 1, rescanned.Version);
        Assert.Equal(SettingApplyStatus.Pending, Assert.Single(rescanned.Apply!).State);
        Assert.True(await application.QueryAsync(database => database.AuditEvents.AnyAsync(
            audit => audit.Action == AuditActions.SettingsChanged && audit.Detail!.Contains("Asked every pxe host"),
            TestContext.Current.CancellationToken)));
        await Eventually(() => application.QueryAsync(database => database.AuditEvents.AnyAsync(
            audit => audit.Action == AuditActions.SettingsApplied && audit.SubjectId == SettingsSectionNames.Pxe && audit.ActorName == SettingsHostStates.Host,
            TestContext.Current.CancellationToken)));
    }

    // While the section has problems nothing is served; configuration that names the interfaces decides that a bind
    // failure at startup stops the host.
    [Fact]
    public void TheListenersServeWhatTheSnapshotSays()
    {
        PxeDesiredSetup stored = PxeSettingsSource.Desired(SettingsSnapshotTests.Build([SettingsSnapshotTests.Stored(SettingsSectionNames.Pxe, """{"interfaces":"eth0"}""")]));
        PxeDesiredSetup broken = PxeSettingsSource.Desired(SettingsSnapshotTests.Build([SettingsSnapshotTests.Stored(SettingsSectionNames.Pxe, """{"tftpMaxWindowSize":0}""")]));
        PxeDesiredSetup configured = PxeSettingsSource.Desired(SettingsSnapshotTests.Build([], ("DDT:Pxe:Interfaces", "eth0")));

        Assert.Equal("eth0", stored.Options!.Interfaces);
        Assert.Equal(3, stored.Version);
        Assert.False(stored.StopHostOnFailure);
        Assert.Null(broken.Options);
        Assert.StartsWith("The pxe settings have problems, so nothing is served until they are fixed: DDT:Pxe:TftpMaxWindowSize:", broken.Refusal, StringComparison.Ordinal);
        Assert.True(configured.StopHostOnFailure);
    }

    // A host writes its state after it applied, while the request that asked goes on.
    internal static async Task Eventually(Func<Task<bool>> condition)
    {
        for (int attempt = 0; attempt < 100 && !await condition(); attempt++)
        {
            await Task.Delay(50, TestContext.Current.CancellationToken);
        }

        Assert.True(await condition());
    }
}
