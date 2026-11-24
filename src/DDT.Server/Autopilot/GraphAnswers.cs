// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Globalization;
using System.Net;
using System.Text.Json;

namespace DDT.Server.Autopilot;

// Reads Graph's importedWindowsAutopilotDeviceIdentity and its importedWindowsAutopilotDeviceIdentityState.
// https://learn.microsoft.com/graph/api/resources/intune-enrollment-importedwindowsautopilotdeviceidentitystate
public static class GraphAnswers
{
    // A busy or failing service, which may answer next time.
    public static void ThrowIfTransient(HttpResponseMessage response)
    {
        ArgumentNullException.ThrowIfNull(response);

        if ((int)response.StatusCode >= 500 || response.StatusCode is HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests)
        {
            throw new HttpRequestException(
                string.Create(CultureInfo.InvariantCulture, $"Microsoft answered {(int)response.StatusCode} {response.ReasonPhrase}."),
                null,
                response.StatusCode);
        }
    }

    // Learn's example of the GET wraps the object in "value", the POST's does not; both are read.
    public static AutopilotImport Import(JsonAnswer answer)
    {
        ArgumentNullException.ThrowIfNull(answer);

        JsonElement? device = answer.Root is { } root && root.TryGetProperty("value", out JsonElement value) && value.ValueKind == JsonValueKind.Object
            ? value
            : answer.Root;
        string id = JsonAnswer.String(device, "id") ?? throw new AutopilotDirectoryException("Microsoft Graph answered without the import's id.");
        JsonElement? state = device is { } found && found.TryGetProperty("state", out JsonElement member) && member.ValueKind == JsonValueKind.Object
            ? member
            : null;

        return JsonAnswer.String(state, "deviceImportStatus") switch
        {
            "complete" => new AutopilotImport(id, AutopilotImportStatus.Complete, null),
            "error" => new AutopilotImport(id, AutopilotImportStatus.Error, Error(state)),
            _ => new AutopilotImport(id, AutopilotImportStatus.Running, null),
        };
    }

    private static string Error(JsonElement? state)
    {
        string? name = JsonAnswer.String(state, "deviceErrorName");
        int? code = state is { } found && found.TryGetProperty("deviceErrorCode", out JsonElement value) && value.TryGetInt32(out int number) ? number : null;

        return (name, code) switch
        {
            (null, null) => "Autopilot gave no reason.",
            (null, { } only) => string.Create(CultureInfo.InvariantCulture, $"Autopilot error {only}."),
            ({ } text, null) => text,
            ({ } text, { } both) => string.Create(CultureInfo.InvariantCulture, $"{text} ({both})"),
        };
    }
}
