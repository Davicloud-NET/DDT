// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Text.Json;
using System.Threading.Channels;
using DDT.Contracts.Settings;
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
