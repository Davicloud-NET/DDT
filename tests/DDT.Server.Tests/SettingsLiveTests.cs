// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Net;
using System.Text.Json;
using System.Threading.Channels;
using DDT.Contracts.Settings;
using DDT.Pxe;
using DDT.Server.Authentication;
using DDT.Server.Live;
using DDT.Server.Settings;
using Xunit;

namespace DDT.Server.Tests;

// Other browsers learn of a save through the hub, with the view the save answered, so they patch what they show.
public sealed class SettingsLiveTests(DdtApplication application) : IClassFixture<DdtApplication>
{
    [Fact]
    public async Task AdministratorsReceiveTheSavedSection()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        await using LiveListener listener = await LiveListener.StartAsync(application, administrator);
        ChannelReader<JsonElement> changes = listener.Listen<JsonElement>(LiveEvents.SettingsChanged);

        SettingsSectionView<LdapSettings> saved = await administrator.SavedAsync<LdapSettings>(
            SettingsSectionNames.Ldap,
            values => values with { DisplayNameAttribute = "cn" });

        JsonElement pushed = await LiveListener.NextAsync(changes, change => change.GetProperty("section").GetString() == SettingsSectionNames.Ldap);

        Assert.Equal(saved.Version, pushed.GetProperty("version").GetInt64());
        Assert.Equal("cn", pushed.GetProperty("values").GetProperty("displayNameAttribute").GetString());
        Assert.False(pushed.GetProperty("secrets").GetProperty("bindPassword").GetProperty("isSet").GetBoolean());
    }

    // A host reports the interfaces it found when it applies the pxe section, and the network boot page lists them.
    [Fact]
    public async Task AdministratorsReceiveTheInterfacesAHostFound()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        await using LiveListener listener = await LiveListener.StartAsync(application, administrator);
        ChannelReader<JsonElement> changes = listener.Listen<JsonElement>(LiveEvents.PxeInterfacesChanged);

        SettingsSectionView<PxeSettings> saved = await administrator.SavedAsync<PxeSettings>(
            SettingsSectionNames.Pxe,
            values => values with { Interfaces = ["lab"] });
        NetworkInterfaceMap interfaces = new("lab", [new ServedInterface(7, "lab", IPAddress.Parse("10.40.0.1"))]);
        await PxeSettingsSource.Create(application.Services).Applied(new PxeApplyResult(saved.Version, true, null, interfaces));

        JsonElement pushed = await LiveListener.NextAsync(changes, hosts => hosts.GetArrayLength() == 1 && hosts[0].GetProperty("interfaces").GetArrayLength() == 1);

        JsonElement found = pushed[0].GetProperty("interfaces")[0];
        Assert.Equal(SettingsHostStates.Host, pushed[0].GetProperty("host").GetString());
        Assert.Equal("lab", found.GetProperty("name").GetString());
        Assert.True(found.GetProperty("served").GetBoolean());
        Assert.Equal("10.40.0.1", found.GetProperty("addresses")[0].GetString());
    }

    // Operators may read deployment and machines, so they receive those, and nothing else of the page.
    [Fact]
    public async Task OperatorsReceiveOnlyTheSectionsTheyMayRead()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        SignedInClient @operator = await application.SignInAsync(DdtRoleNames.Operator);
        await using LiveListener listener = await LiveListener.StartAsync(application, @operator);
        ChannelReader<JsonElement> changes = listener.Listen<JsonElement>(LiveEvents.SettingsChanged);

        await administrator.SavedAsync<LoggingSettings>(
            SettingsSectionNames.Logging,
            values => values with { LogLevel = new Dictionary<string, string>(values.LogLevel) { ["DDT.Live.Test"] = "Debug" } });
        await administrator.SavedAsync<DeploymentSettings>(SettingsSectionNames.Deployment, values => values with { Keyboard = "0407:00000407" });

        JsonElement pushed = await LiveListener.NextAsync(changes);

        Assert.Equal(SettingsSectionNames.Deployment, pushed.GetProperty("section").GetString());
        Assert.False(changes.TryRead(out _));
    }
}
