// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using DDT.Contracts.Agents;
using DDT.Contracts.Settings;
using DDT.Server.Machines;
using DDT.Server.Settings;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DDT.Server.Tests;

// A release puts an agent and a console next to the server. Machines get those while nothing is uploaded, an upload
// wins, and removing the upload brings the server's own back.
public sealed class BundledReleaseTests
{
    private const string AgentSettings = "/api/settings/agent";
    private const string ConsoleSettings = "/api/settings/agent/console";

    [Fact]
    public async Task ANewServerOffersTheAgentItCameWith()
    {
        using BundledReleaseApplication application = new();
        byte[] bundled = Executables.Versioned("0.4.0");
        await File.WriteAllBytesAsync(application.Bundled.AgentPath, bundled, TestContext.Current.CancellationToken);
        SignedInClient administrator = await application.AdministratorAsync();
        using HttpClient machine = application.CreateClient();

        AgentBinaryView view = await ReadAsync(administrator, AgentSettings);
        AgentRelease? release = await machine.GetFromJsonAsync<AgentRelease>("api/agents/release", TestContext.Current.CancellationToken);

        Assert.Equal(AgentBinarySource.Bundled, view.Source);
        Assert.Equal("0.4.0", view.Version);
        Assert.Equal(ConsolePackages.Sha256(bundled), view.Sha256);
        Assert.Null(view.UploadedBy);
        Assert.Null(view.NewerBundledVersion);
        Assert.Equal(view.Sha256, release?.Sha256);
        Assert.Equal(bundled, await machine.GetByteArrayAsync(new Uri("api/agents/release/binary", UriKind.Relative), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task AnUploadWinsUntilItIsRemovedAndTheServersNewerAgentIsNamed()
    {
        using BundledReleaseApplication application = new();
        await File.WriteAllBytesAsync(application.Bundled.AgentPath, Executables.Versioned("0.4.0"), TestContext.Current.CancellationToken);
        SignedInClient administrator = await application.AdministratorAsync();
        byte[] older = Executables.Versioned("0.3.5");

        AgentBinaryView uploaded = await RegisteredMachine.ReadAsync<AgentBinaryView>(
            await UploadAsync(administrator, AgentSettings + "/binary", older, "application/octet-stream"));

        Assert.Equal(AgentBinarySource.Uploaded, uploaded.Source);
        Assert.Equal("0.3.5", uploaded.Version);
        Assert.Equal("0.4.0", uploaded.NewerBundledVersion);
        Assert.NotNull(uploaded.UploadedBy);

        // An agent newer than the server's own is nothing to point out.
        AgentBinaryView newer = await RegisteredMachine.ReadAsync<AgentBinaryView>(
            await UploadAsync(administrator, AgentSettings + "/binary", Executables.Versioned("0.5.0"), "application/octet-stream"));
        Assert.Null(newer.NewerBundledVersion);

        AgentBinaryView removed = await RegisteredMachine.ReadAsync<AgentBinaryView>(await administrator.DeleteAsync(AgentSettings + "/binary"));

        Assert.Equal(AgentBinarySource.Bundled, removed.Source);
        Assert.Equal("0.4.0", removed.Version);
        Assert.Null(removed.UploadedBy);
        Assert.Equal(removed, await ReadAsync(administrator, AgentSettings));
        Assert.True(await application.QueryAsync(database => database.AuditEvents.AnyAsync(
            audit => audit.Action == AuditActions.AgentUploadRemoved && audit.SubjectId == newer.Sha256,
            TestContext.Current.CancellationToken)));
        Assert.Empty(Directory.GetFiles(Path.Combine(application.StorePath, "agent")));

        // Nothing is uploaded any more.
        Assert.Equal(HttpStatusCode.NotFound, (await administrator.DeleteAsync(AgentSettings + "/binary")).StatusCode);
    }

    [Fact]
    public async Task WithoutABundledAgentRemovingTheUploadLeavesNone()
    {
        using BundledReleaseApplication application = new();
        SignedInClient administrator = await application.AdministratorAsync();
        await UploadAsync(administrator, AgentSettings + "/binary", Executables.Versioned("0.3.5"), "application/octet-stream");

        AgentBinaryView removed = await RegisteredMachine.ReadAsync<AgentBinaryView>(await administrator.DeleteAsync(AgentSettings + "/binary"));

        Assert.Equal(AgentBinarySource.None, removed.Source);
        Assert.Null(removed.Sha256);
        Assert.Equal(HttpStatusCode.NotFound, (await application.CreateClient().GetAsync(new Uri("api/agents/release", UriKind.Relative), TestContext.Current.CancellationToken)).StatusCode);
    }

    [Fact]
    public async Task OnlyAnAdministratorRemovesAnUpload()
    {
        using BundledReleaseApplication application = new();
        SignedInClient @operator = await application.SignInAsync(DDT.Server.Authentication.DdtRoleNames.Operator);

        Assert.Equal(HttpStatusCode.Forbidden, (await @operator.DeleteAsync(AgentSettings + "/binary")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await @operator.DeleteAsync(ConsoleSettings)).StatusCode);
    }

    [Fact]
    public async Task TheConsoleTheServerCameWithIsOfferedTheSameWay()
    {
        using BundledReleaseApplication application = new();
        (string Path, byte[] Content)[] bundled = Console("0.4.0");
        await File.WriteAllBytesAsync(application.Bundled.ConsolePath, ConsolePackages.Zip(bundled), TestContext.Current.CancellationToken);
        SignedInClient administrator = await application.AdministratorAsync();
        using HttpClient machine = application.CreateClient();

        AgentBinaryView view = await ReadAsync(administrator, ConsoleSettings);
        Assert.Equal(AgentBinarySource.Bundled, view.Source);
        Assert.Equal("0.4.0", view.Version);
        Assert.Equal(ConsolePackages.Sha256(bundled[0].Content), view.Sha256);
        Assert.Equal(
            bundled[0].Content,
            await machine.GetByteArrayAsync(new Uri("api/agents/release/console/ddt-console.exe", UriKind.Relative), TestContext.Current.CancellationToken));

        AgentBinaryView uploaded = await RegisteredMachine.ReadAsync<AgentBinaryView>(
            await UploadAsync(administrator, ConsoleSettings, ConsolePackages.Zip(Console("0.3.0")), "application/zip"));
        Assert.Equal(AgentBinarySource.Uploaded, uploaded.Source);
        Assert.Equal("0.3.0", uploaded.Version);
        Assert.Equal("0.4.0", uploaded.NewerBundledVersion);

        AgentBinaryView removed = await RegisteredMachine.ReadAsync<AgentBinaryView>(await administrator.DeleteAsync(ConsoleSettings));
        Assert.Equal(AgentBinarySource.Bundled, removed.Source);
        Assert.Equal(view.Sha256, removed.Sha256);
        Assert.True(await application.QueryAsync(database => database.AuditEvents.AnyAsync(
            audit => audit.Action == AuditActions.ConsoleUploadRemoved,
            TestContext.Current.CancellationToken)));
    }

    // The console's three files, with a version in ddt-console.exe
    private static (string Path, byte[] Content)[] Console(string version) =>
    [
        ("ddt-console.exe", Executables.Versioned(version)),
        ("libSkiaSharp.dll", ConsolePackages.Executable(3000)),
        ("libHarfBuzzSharp.dll", ConsolePackages.Executable(2000)),
    ];

    private static async Task<AgentBinaryView> ReadAsync(SignedInClient client, string path) =>
        await RegisteredMachine.ReadAsync<AgentBinaryView>(await client.GetAsync(path));

    private static async Task<HttpResponseMessage> UploadAsync(SignedInClient client, string path, byte[] content, string type)
    {
        using HttpRequestMessage request = new(HttpMethod.Put, new Uri(path, UriKind.Relative))
        {
            Content = new ByteArrayContent(content) { Headers = { ContentType = new MediaTypeHeaderValue(type) } },
        };
        request.Headers.Add(ReauthenticationTokens.HeaderName, await client.TokenAsync());

        return await client.SendAsync(request, TestContext.Current.CancellationToken);
    }
}
