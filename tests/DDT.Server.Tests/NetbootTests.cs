// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Net;
using System.Net.Http.Json;
using DDT.Contracts.Netboot;
using DDT.Pxe;
using DDT.Server.Authentication;
using DDT.Server.BootImage;
using DDT.Server.Certificates;
using DDT.Server.Machines;
using DDT.Server.Settings;
using Microsoft.EntityFrameworkCore;
using Xunit;
using static DDT.Server.Tests.AccountRequests;

namespace DDT.Server.Tests;

// Netboot next to the server's own DHCP server and WDS, with a helper that is a test's own.
public sealed class NetbootTests : IDisposable
{
    private const string Netboot = "/api/netboot";

    private readonly BootImageBuildApplication _application = new();

    [Fact]
    public async Task AnAdministratorSeesWhoHoldsThePortsAndWhatADhcpServerHasToSay()
    {
        SignedInClient administrator = await _application.AdministratorAsync();
        using SignedInClient operatorClient = await _application.SignInAsync(DdtRoleNames.Operator);

        NetbootNeighbours neighbours = await RegisteredMachine.ReadAsync<NetbootNeighbours>(await administrator.GetAsync(Netboot));

        Assert.Equal((ServerNames.DnsName(), PxeSetup.DefaultBootFile, true), (neighbours.BootServer, neighbours.BootFile, neighbours.Helper));
        Assert.Equal(OperatingSystem.IsWindows() ? [67, 69, 4011] : null, neighbours.Ports?.Select(port => port.Port));
        Assert.Equal(HttpStatusCode.Forbidden, (await operatorClient.GetAsync(Netboot)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await operatorClient.PostAsync($"{Netboot}/wds/replace")).StatusCode);
    }

    [Fact]
    public async Task TheHelperSetsOptions66And67ForTheChosenScopesAfterThePassword()
    {
        SignedInClient administrator = await _application.AdministratorAsync();
        _application.Helper.Lines.Clear();
        _application.Helper.Lines.Add("""[{"scopeId":"10.0.100.0","name":"Lab","active":true,"bootServer":null,"bootFile":"old.efi"}]""");

        DhcpScope scope = Assert.Single(await RegisteredMachine.ReadAsync<List<DhcpScope>>(await administrator.GetAsync($"{Netboot}/dhcp-scopes")));
        Assert.Equal(("10.0.100.0", "Lab", true, null, "old.efi"), (scope.ScopeId, scope.Name, scope.Active, scope.BootServer, scope.BootFile));

        SetDhcpOptionsRequest request = new(["10.0.100.0"]);
        Assert.Equal(HttpStatusCode.Forbidden, (await administrator.PostAsync($"{Netboot}/dhcp-options", request)).StatusCode);

        string proof = await administrator.TokenAsync();
        Assert.Equal(HttpStatusCode.BadRequest, (await SendAsync(administrator, $"{Netboot}/dhcp-options", proof, new SetDhcpOptionsRequest(["10.0.100.0; calc"]))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await SendAsync(administrator, $"{Netboot}/dhcp-options", proof, new SetDhcpOptionsRequest([]))).StatusCode);
        Assert.DoesNotContain(_application.Helper.Requests, sent => sent.Kind == HelperRequest.DhcpOptions);

        using HttpResponseMessage set = await SendAsync(administrator, $"{Netboot}/dhcp-options", proof, request);
        Assert.Equal(HttpStatusCode.OK, set.StatusCode);

        HelperRequest change = Assert.Single(_application.Helper.Requests, sent => sent.Kind == HelperRequest.DhcpOptions);
        Assert.Equal((ServerNames.DnsName(), PxeSetup.DefaultBootFile), (change.BootServer, change.BootFile));
        Assert.Equal(["10.0.100.0"], change.Scopes);
        Assert.Equal(1, await _application.QueryAsync(database =>
            database.AuditEvents.CountAsync(audit => audit.Action == AuditActions.DhcpOptionsSet, TestContext.Current.CancellationToken)));
    }

    [Theory]
    [InlineData("replace", HelperRequest.WdsReplace, AuditActions.WdsReplaced)]
    [InlineData("restore", HelperRequest.WdsRestore, AuditActions.WdsRestored)]
    [InlineData("boot-image", HelperRequest.WdsBootImage, AuditActions.WdsBootImageAdded)]
    public async Task TheHelperChangesWdsAndARefusalIsToldAsItIs(string route, string kind, string action)
    {
        SignedInClient administrator = await _application.AdministratorAsync();
        string proof = await administrator.TokenAsync();

        Assert.Equal(HttpStatusCode.Forbidden, (await administrator.PostAsync($"{Netboot}/wds/{route}")).StatusCode);

        using HttpResponseMessage done = await SendAsync(administrator, $"{Netboot}/wds/{route}", proof, null);
        Assert.Equal(HttpStatusCode.OK, done.StatusCode);
        Assert.Single(_application.Helper.Requests, sent => sent.Kind == kind);
        Assert.Equal(1, await _application.QueryAsync(database =>
            database.AuditEvents.CountAsync(audit => audit.Action == action, TestContext.Current.CancellationToken)));

        _application.Helper.Problem = "Cannot stop service WDSServer.";
        using HttpResponseMessage failed = await SendAsync(administrator, $"{Netboot}/wds/{route}", proof, null);
        Assert.Equal((HttpStatusCode.BadGateway, "netboot.helperFailed"), (failed.StatusCode, await CodeAsync(failed)));

        _application.Helper.Available = false;
        Assert.Equal("netboot.noHelper", await CodeAsync(await SendAsync(administrator, $"{Netboot}/wds/{route}", proof, null)));
        Assert.Equal("netboot.noHelper", await CodeAsync(await administrator.GetAsync($"{Netboot}/dhcp-scopes")));
    }

    public void Dispose() => _application.Dispose();

    private static async Task<HttpResponseMessage> SendAsync(SignedInClient client, string path, string proof, object? body)
    {
        using HttpRequestMessage request = new(HttpMethod.Post, new Uri(path, UriKind.Relative));
        request.Headers.Add(ReauthenticationTokens.HeaderName, proof);

        if (body is not null)
        {
            request.Content = JsonContent.Create(body, body.GetType(), options: TestJson.Options);
        }

        return await client.SendAsync(request, TestContext.Current.CancellationToken);
    }
}
