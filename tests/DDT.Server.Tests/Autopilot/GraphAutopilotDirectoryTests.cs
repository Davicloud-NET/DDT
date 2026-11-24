// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Net;
using System.Text.Json;
using DDT.Server.Autopilot;
using Xunit;

namespace DDT.Server.Tests.Autopilot;

// The requests as Microsoft Learn gives them, against a handler that stands in for Microsoft.
public sealed class GraphAutopilotDirectoryTests
{
    private const string Secret = "s3cr3t~value&more";
    private const string ImportsUrl = "https://graph.microsoft.com/v1.0/deviceManagement/importedWindowsAutopilotDeviceIdentities";

    private static readonly AutopilotOptions s_account = new()
    {
        TenantId = "contoso.onmicrosoft.com",
        ClientId = "00001111-aaaa-2222-bbbb-3333cccc4444",
        ClientSecret = Secret,
    };

    private static readonly AutopilotDevice s_device = new("SN-1", Convert.ToBase64String(new byte[3000]), "Sales");

    private readonly MicrosoftHandler _microsoft = new();
    private readonly ManualTimeProvider _time = new();
    private readonly GraphTokens _tokens;

    public GraphAutopilotDirectoryTests() => _tokens = new GraphTokens(_microsoft, _time);

    private GraphAutopilotDirectory Directory => new(_microsoft, _tokens);

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private static string Created(string status, string extra = "") =>
        $$$"""{"@odata.type":"#microsoft.graph.importedWindowsAutopilotDeviceIdentity","id":"985b4f49-4f49-985b-494f-5b98494f5b98","serialNumber":"SN-1","state":{"deviceImportStatus":"{{{status}}}"{{{extra}}}}}""";

    [Fact]
    public async Task GetsAnAppTokenThenPostsTheDevice()
    {
        _microsoft.Token().Then(HttpStatusCode.Created, Created("pending"));

        AutopilotImport import = await Directory.ImportAsync(s_account, s_device, Cancellation);

        Assert.Equal(new AutopilotImport("985b4f49-4f49-985b-494f-5b98494f5b98", AutopilotImportStatus.Running, null), import);
        (HttpMethod method, Uri uri, string? authorization, string body) = _microsoft.Requests[0];
        Assert.Equal((HttpMethod.Post, "https://login.microsoftonline.com/contoso.onmicrosoft.com/oauth2/v2.0/token", null), (method, uri.AbsoluteUri, authorization));
        Assert.Equal(
            "client_id=00001111-aaaa-2222-bbbb-3333cccc4444&scope=https%3A%2F%2Fgraph.microsoft.com%2F.default&client_secret=s3cr3t~value%26more&grant_type=client_credentials",
            body);

        (method, uri, authorization, body) = _microsoft.Requests[1];
        Assert.Equal((HttpMethod.Post, ImportsUrl, "Bearer token-1"), (method, uri.AbsoluteUri, authorization));
        using JsonDocument sent = JsonDocument.Parse(body);
        Assert.Equal(
            ["@odata.type", "serialNumber", "hardwareIdentifier", "groupTag"],
            sent.RootElement.EnumerateObject().Select(property => property.Name));
        Assert.Equal(s_device.Hash, sent.RootElement.GetProperty("hardwareIdentifier").GetString());
        Assert.Equal("Sales", sent.RootElement.GetProperty("groupTag").GetString());
    }

    [Fact]
    public async Task LeavesTheGroupTagOutWhenThereIsNone()
    {
        _microsoft.Token().Then(HttpStatusCode.Created, Created("pending"));

        await Directory.ImportAsync(s_account, s_device with { GroupTag = null }, Cancellation);

        using JsonDocument sent = JsonDocument.Parse(_microsoft.Requests[1].Body);
        Assert.False(sent.RootElement.TryGetProperty("groupTag", out _));
    }

    // Learn's example of the GET wraps the device in "value".
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ReadsTheImportStateWithOrWithoutTheValueWrapper(bool wrapped)
    {
        string device = Created("error", ""","deviceErrorCode":806,"deviceErrorName":"ZtdDeviceAlreadyAssigned" """);
        _microsoft.Token().Then(HttpStatusCode.OK, wrapped ? $$"""{"value":{{device}}}""" : device);

        AutopilotImport import = await Directory.ReadAsync(s_account, "985b4f49-4f49-985b-494f-5b98494f5b98", Cancellation);

        Assert.Equal((AutopilotImportStatus.Error, "ZtdDeviceAlreadyAssigned (806)"), (import.Status, import.Error));
        Assert.Equal((HttpMethod.Get, $"{ImportsUrl}/985b4f49-4f49-985b-494f-5b98494f5b98"), (_microsoft.Requests[1].Method, _microsoft.Requests[1].Uri.AbsoluteUri));
    }

    [Theory]
    [InlineData("complete", AutopilotImportStatus.Complete)]
    [InlineData("pending", AutopilotImportStatus.Running)]
    [InlineData("partial", AutopilotImportStatus.Running)]
    [InlineData("unknown", AutopilotImportStatus.Running)]
    public async Task MapsEachImportStatus(string status, AutopilotImportStatus expected)
    {
        _microsoft.Token().Then(HttpStatusCode.OK, Created(status));

        Assert.Equal(expected, (await Directory.ReadAsync(s_account, "985b4f49-4f49-985b-494f-5b98494f5b98", Cancellation)).Status);
    }

    [Fact]
    public async Task KeepsTheTokenUntilShortlyBeforeItExpires()
    {
        _microsoft.Token("token-1").Then(HttpStatusCode.OK, Created("pending")).Then(HttpStatusCode.OK, Created("pending"))
            .Token("token-2").Then(HttpStatusCode.OK, Created("complete"));

        await Directory.ReadAsync(s_account, "a", Cancellation);
        _time.Advance(TimeSpan.FromMinutes(50));
        await Directory.ReadAsync(s_account, "a", Cancellation);
        _time.Advance(TimeSpan.FromMinutes(5));
        await Directory.ReadAsync(s_account, "a", Cancellation);

        Assert.Equal(
            [null, "Bearer token-1", "Bearer token-1", null, "Bearer token-2"],
            _microsoft.Requests.Select(request => request.Authorization));
    }

    [Fact]
    public async Task ARefusedTokenSaysWhyWithoutTheSecret()
    {
        _microsoft.Then(
            HttpStatusCode.Unauthorized,
            """{"error":"invalid_client","error_description":"AADSTS7000215: Invalid client secret provided.\r\nTrace ID: 0000aaaa\r\nCorrelation ID: bbbb"}""");

        AutopilotDirectoryException refused = await Assert.ThrowsAsync<AutopilotDirectoryException>(() => Directory.ImportAsync(s_account, s_device, Cancellation));

        Assert.Equal("Microsoft Entra ID gave no token (401): AADSTS7000215: Invalid client secret provided.", refused.Message);
        Assert.DoesNotContain(Secret, refused.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task AMissingPermissionNamesTheOneTheAppNeeds()
    {
        _microsoft.Token().Then(HttpStatusCode.Forbidden, """{"error":{"code":"Forbidden","message":"Application is not authorized to perform this operation."}}""");

        AutopilotDirectoryException refused = await Assert.ThrowsAsync<AutopilotDirectoryException>(() => Directory.ImportAsync(s_account, s_device, Cancellation));

        Assert.Equal(
            "Microsoft Graph refused the request (403): Forbidden: Application is not authorized to perform this operation. "
                + "The app registration needs the application permission DeviceManagementServiceConfig.ReadWrite.All, with admin consent.",
            refused.Message);
        Assert.DoesNotContain(s_device.Hash, refused.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    public async Task ABusyServiceMayPassOnItsOwn(HttpStatusCode status)
    {
        _microsoft.Token().Then(status, string.Empty);

        HttpRequestException busy = await Assert.ThrowsAsync<HttpRequestException>(() => Directory.ImportAsync(s_account, s_device, Cancellation));

        Assert.Equal(status, busy.StatusCode);
    }
}
