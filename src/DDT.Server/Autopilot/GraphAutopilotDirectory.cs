// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;

namespace DDT.Server.Autopilot;

// Autopilot's device import through Microsoft Graph v1.0, on plain HttpClient. The app registration needs the
// application permission DeviceManagementServiceConfig.ReadWrite.All.
// https://learn.microsoft.com/graph/api/intune-enrollment-importedwindowsautopilotdeviceidentity-create
public sealed class GraphAutopilotDirectory(IHttpClientFactory clients, GraphTokens tokens) : IAutopilotDirectory
{
    public const string Imports = "https://graph.microsoft.com/v1.0/deviceManagement/importedWindowsAutopilotDeviceIdentities";

    public const string Permission = "DeviceManagementServiceConfig.ReadWrite.All";

    public async Task<AutopilotImport> ImportAsync(AutopilotOptions account, AutopilotDevice device, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(device);

        JsonObject body = new()
        {
            ["@odata.type"] = "#microsoft.graph.importedWindowsAutopilotDeviceIdentity",
            ["serialNumber"] = device.SerialNumber,
            ["hardwareIdentifier"] = device.Hash,
        };

        if (!string.IsNullOrEmpty(device.GroupTag))
        {
            body["groupTag"] = device.GroupTag;
        }

        using HttpRequestMessage request = new(HttpMethod.Post, Imports)
        {
            Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json"),
        };

        return await SendAsync(account, request, cancellationToken).ConfigureAwait(false);
    }

    public async Task<AutopilotImport> ReadAsync(AutopilotOptions account, string importId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(importId);

        using HttpRequestMessage request = new(HttpMethod.Get, $"{Imports}/{Uri.EscapeDataString(importId)}");

        return await SendAsync(account, request, cancellationToken).ConfigureAwait(false);
    }

    private async Task<AutopilotImport> SendAsync(AutopilotOptions account, HttpRequestMessage request, CancellationToken cancellationToken)
    {
        request.Headers.Authorization = await tokens.HeaderAsync(account, cancellationToken).ConfigureAwait(false);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        using HttpClient client = clients.CreateClient(AutopilotServiceCollectionExtensions.GraphClient);
        using HttpResponseMessage response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);
        using JsonAnswer answer = await JsonAnswer.ReadAsync(response, cancellationToken).ConfigureAwait(false);

        if (response.IsSuccessStatusCode)
        {
            return GraphAnswers.Import(answer);
        }

        GraphAnswers.ThrowIfTransient(response);
        string permission = response.StatusCode == HttpStatusCode.Forbidden
            ? $" The app registration needs the application permission {Permission}, with admin consent."
            : string.Empty;

        throw new AutopilotDirectoryException($"Microsoft Graph refused the request ({(int)response.StatusCode}): {answer.GraphError().TrimEnd('.')}.{permission}");
    }
}
