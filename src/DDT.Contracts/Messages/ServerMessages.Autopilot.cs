// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Contracts.Messages;

public static partial class ServerMessages
{
    // The Autopilot settings and the machine's Autopilot actions.

    public static readonly MessageTemplate SettingsAutopilotTenantId = Define(
        "settings.autopilot.tenantId",
        "Use the tenant ID, a GUID, or the tenant's domain, such as contoso.onmicrosoft.com.");

    public static readonly MessageTemplate SettingsAutopilotClientId = Define(
        "settings.autopilot.clientId",
        "Use the application (client) ID of the app registration, a GUID.");

    public static readonly MessageTemplate SettingsAutopilotBoth = Define(
        "settings.autopilot.both",
        "Set the tenant and the client ID together, or neither.");

    public static readonly MessageTemplate SettingsAutopilotGroupTag = Define(
        "settings.autopilot.groupTag",
        "A group tag has at most {max} characters, without commas or quotation marks.");

    public static readonly MessageTemplate AutopilotNoHash = Define(
        "autopilot.noHash",
        "No hardware hash was collected from this machine yet.");

    public static readonly MessageTemplate AutopilotNotSetUp = Define(
        "autopilot.notSetUp",
        "Autopilot uploads are not set up. Enter the tenant, the client ID and the client secret in the Autopilot settings.");

    public static readonly MessageTemplate AutopilotNoHashes = Define(
        "autopilot.noHashes",
        "None of these machines has a hardware hash.");

    public static readonly MessageTemplate AutopilotTooMany = Define(
        "autopilot.tooMany",
        "Intune imports at most {max} devices from one file.");
}
