// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Channels;
using DDT.Contracts.Agents;
using DDT.Contracts.Settings;
using DDT.Server.Live;
using DDT.Server.Machines;
using DDT.Server.Settings;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DDT.Server.Tests;

// The agent that every netbooting machine runs as SYSTEM, uploaded on the settings page. The upload needs a fresh proof
// of identity and takes only a Windows executable. It's refused while the configuration names the file.
public sealed class AgentUploadTests(DdtApplication application) : IClassFixture<DdtApplication>
{
    [Fact]
    public async Task AnUploadReplacesTheAgentMachinesSwitchTo()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        byte[] agent = [(byte)'M', (byte)'Z', .. RandomNumberGenerator.GetBytes(4096)];
        await using LiveListener listener = await LiveListener.StartAsync(application, administrator);
        ChannelReader<JsonElement> changes = listener.Listen<JsonElement>(LiveEvents.AgentChanged);

        Assert.Equal(AgentBinarySource.None, (await RegisteredMachine.ReadAsync<AgentBinaryView>(await administrator.GetAsync("/api/settings/agent"))).Source);

        AgentBinaryView uploaded = await RegisteredMachine.ReadAsync<AgentBinaryView>(
            await UploadAsync(administrator, agent, await administrator.TokenAsync()));

        string sha256 = Convert.ToHexStringLower(SHA256.HashData(agent));
        Assert.Equal(sha256, uploaded.Sha256);
        Assert.Equal(agent.Length, uploaded.Size);
        Assert.Equal(AgentBinarySource.Uploaded, uploaded.Source);

        // The answer is the same view a read returns from now on. So the page can use it without reading it again.
        AgentBinaryView view = await RegisteredMachine.ReadAsync<AgentBinaryView>(await administrator.GetAsync("/api/settings/agent"));
        Assert.Equal(AgentBinarySource.Uploaded, view.Source);
        Assert.Equal(sha256, view.Sha256);
        Assert.NotNull(view.UploadedBy);
        Assert.Equal(view.UploadedBy, uploaded.UploadedBy);
        Assert.NotNull(view.UploadedUtc);
        Assert.NotNull(uploaded.UploadedUtc);

        // Other administrators' pages get it from the hub.
        JsonElement pushed = await LiveListener.NextAsync(changes);
        Assert.Equal(sha256, pushed.GetProperty("sha256").GetString());
        Assert.Equal("Uploaded", pushed.GetProperty("source").GetString());

        // What agents are told to switch to.
        AgentRelease release = (await application.Services.GetRequiredService<AgentReleaseStore>().CurrentAsync(TestContext.Current.CancellationToken))!;
        Assert.Equal(sha256, release.Sha256);

        Assert.True(await application.QueryAsync(database => database.AuditEvents.AnyAsync(
            audit => audit.Action == AuditActions.AgentUploaded && audit.SubjectId == sha256,
            TestContext.Current.CancellationToken)));
        Assert.Empty(Directory.GetFiles(Path.Combine(application.StorePath, "agent"), "*.upload"));
    }

    [Fact]
    public async Task AnUploadNeedsAFreshProofOfIdentity()
    {
        SignedInClient administrator = await application.AdministratorAsync();

        HttpResponseMessage response = await UploadAsync(administrator, [(byte)'M', (byte)'Z', 1, 2, 3], null);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task OnlyAWindowsExecutableIsTaken()
    {
        SignedInClient administrator = await application.AdministratorAsync();

        HttpResponseMessage response = await UploadAsync(administrator, "#!/bin/sh\n"u8.ToArray(), await administrator.TokenAsync());

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("binary", (await SettingsRequests.ProblemsAsync(response)).Errors.Keys);
        Assert.Empty(Directory.GetFiles(Path.Combine(application.StorePath, "agent"), "*.upload"));
    }

    [Fact]
    public async Task OnlyAnAdministratorUploads()
    {
        SignedInClient @operator = await application.SignInAsync(DDT.Server.Authentication.DdtRoleNames.Operator);

        Assert.Equal(HttpStatusCode.Forbidden, (await @operator.GetAsync("/api/settings/agent")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await UploadAsync(@operator, [(byte)'M', (byte)'Z'], null)).StatusCode);
    }

    // DDT:Agent:BinaryPath is a development override, and the page cannot replace a file configuration names.
    [Fact]
    public async Task AConfiguredAgentCannotBeReplaced()
    {
        using AgentReleaseApplication configured = new();
        SignedInClient administrator = await configured.AdministratorAsync();

        HttpResponseMessage response = await UploadAsync(administrator, [(byte)'M', (byte)'Z'], await administrator.TokenAsync());

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(AgentBinarySource.Configuration, (await RegisteredMachine.ReadAsync<AgentBinaryView>(await administrator.GetAsync("/api/settings/agent"))).Source);
    }

    private static async Task<HttpResponseMessage> UploadAsync(SignedInClient client, byte[] content, string? token)
    {
        using HttpRequestMessage request = new(HttpMethod.Put, new Uri("/api/settings/agent/binary", UriKind.Relative))
        {
            Content = new ByteArrayContent(content) { Headers = { ContentType = new MediaTypeHeaderValue("application/octet-stream") } },
        };

        if (token is not null)
        {
            request.Headers.Add(ReauthenticationTokens.HeaderName, token);
        }

        return await client.SendAsync(request, TestContext.Current.CancellationToken);
    }
}
