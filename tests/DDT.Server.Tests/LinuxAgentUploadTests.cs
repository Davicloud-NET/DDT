// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Channels;
using DDT.Contracts.Agents;
using DDT.Contracts.Settings;
using DDT.Server.Live;
using DDT.Server.Machines;
using DDT.Server.Settings;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DDT.Server.Tests;

// The agent that every machine in the Linux boot environment runs as root, uploaded on the settings page. The upload
// needs a fresh proof of identity and takes only a 64-bit Linux program for x86-64.
public sealed class LinuxAgentUploadTests(DdtApplication application) : IClassFixture<DdtApplication>
{
    private const string Settings = "/api/settings/agent/linux";
    private const string Release = "api/agents/releases/linux-x64";

    [Fact]
    public async Task AnUploadReplacesTheAgentMachinesInTheLinuxBootEnvironmentSwitchTo()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        using HttpClient machine = application.CreateClient();
        byte[] agent = LinuxExecutables.Versioned("26.1.512");
        await using LiveListener listener = await LiveListener.StartAsync(application, administrator);
        ChannelReader<JsonElement> changes = listener.Listen<JsonElement>(LiveEvents.LinuxAgentChanged);

        Assert.Equal(AgentBinarySource.None, (await RegisteredMachine.ReadAsync<AgentBinaryView>(await administrator.GetAsync(Settings))).Source);
        Assert.Equal(HttpStatusCode.NotFound, (await machine.GetAsync(new Uri(Release, UriKind.Relative), TestContext.Current.CancellationToken)).StatusCode);

        AgentBinaryView uploaded = await RegisteredMachine.ReadAsync<AgentBinaryView>(
            await UploadAsync(administrator, agent, await administrator.TokenAsync()));

        string sha256 = Convert.ToHexStringLower(SHA256.HashData(agent));
        Assert.Equal((sha256, agent.Length, AgentBinarySource.Uploaded, "26.1.512"), (uploaded.Sha256, uploaded.Size, uploaded.Source, uploaded.Version));
        Assert.NotNull(uploaded.UploadedBy);
        Assert.NotNull(uploaded.UploadedUtc);
        Assert.Equal(uploaded, await RegisteredMachine.ReadAsync<AgentBinaryView>(await administrator.GetAsync(Settings)));

        // The pages of other administrators get it from the hub.
        JsonElement pushed = await LiveListener.NextAsync(changes);
        Assert.Equal(sha256, pushed.GetProperty("sha256").GetString());

        // What agents in the Linux boot environment are told to switch to, and what they download.
        AgentRelease? release = await machine.GetFromJsonAsync<AgentRelease>(Release, TestContext.Current.CancellationToken);
        Assert.Equal(new AgentRelease(sha256, agent.Length), release);
        Assert.Equal(agent, await machine.GetByteArrayAsync(new Uri(Release + "/binary", UriKind.Relative), TestContext.Current.CancellationToken));

        Assert.True(await application.QueryAsync(database => database.AuditEvents.AnyAsync(
            audit => audit.Action == AuditActions.LinuxAgentUploaded && audit.SubjectId == sha256,
            TestContext.Current.CancellationToken)));
        Assert.Equal(["ddt-agent-linux-x64"], Directory.GetFiles(Path.Combine(application.StorePath, "agent")).Select(Path.GetFileName));

        // The agent for Windows PE is another one, and none was uploaded.
        Assert.Equal(AgentBinarySource.None, (await RegisteredMachine.ReadAsync<AgentBinaryView>(await administrator.GetAsync("/api/settings/agent"))).Source);
        Assert.Equal(HttpStatusCode.NotFound, (await machine.GetAsync(new Uri("api/agents/release", UriKind.Relative), TestContext.Current.CancellationToken)).StatusCode);

        AgentBinaryView removed = await RegisteredMachine.ReadAsync<AgentBinaryView>(await administrator.DeleteAsync(Settings + "/binary"));
        Assert.Equal(AgentBinarySource.None, removed.Source);
        Assert.Equal(HttpStatusCode.NotFound, (await administrator.DeleteAsync(Settings + "/binary")).StatusCode);
        Assert.True(await application.QueryAsync(database => database.AuditEvents.AnyAsync(
            audit => audit.Action == AuditActions.LinuxAgentUploadRemoved && audit.SubjectId == sha256,
            TestContext.Current.CancellationToken)));
    }

    [Fact]
    public async Task AnUploadNeedsAFreshProofOfIdentity()
    {
        SignedInClient administrator = await application.AdministratorAsync();

        HttpResponseMessage response = await UploadAsync(administrator, LinuxExecutables.Versioned("26.1.512"), null);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task OnlyALinuxProgramForX64IsTaken()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        string token = await administrator.TokenAsync();
        byte[][] refused =
        [
            Executables.Versioned("26.1.512"),
            "#!/bin/sh\necho not an agent\n"u8.ToArray(),
            [.. LinuxExecutables.Header(machine: 183), .. RandomNumberGenerator.GetBytes(512)],
            [.. LinuxExecutables.Header(bits: 1, machine: 3), .. RandomNumberGenerator.GetBytes(512)],
            LinuxExecutables.Header()[..12],
        ];

        foreach (byte[] file in refused)
        {
            HttpResponseMessage response = await UploadAsync(administrator, file, token);

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Contains("binary", (await SettingsRequests.ProblemsAsync(response)).Errors.Keys);
        }

        // Nothing of a refused upload stays, and none became the agent.
        string folder = Path.Combine(application.StorePath, "agent");
        Assert.False(Directory.Exists(folder) && Directory.GetFiles(folder, "*.upload").Length > 0);
        Assert.Null((await RegisteredMachine.ReadAsync<AgentBinaryView>(await administrator.GetAsync(Settings))).Sha256);
    }

    [Fact]
    public async Task OnlyAnAdministratorUploads()
    {
        SignedInClient @operator = await application.SignInAsync(DDT.Server.Authentication.DdtRoleNames.Operator);

        Assert.Equal(HttpStatusCode.Forbidden, (await @operator.GetAsync(Settings)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await UploadAsync(@operator, LinuxExecutables.Header(), null)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await @operator.DeleteAsync(Settings + "/binary")).StatusCode);
    }

    // DDT:Agent:LinuxBinaryPath is a development override, and the page cannot replace a file configuration names.
    [Fact]
    public async Task AConfiguredAgentCannotBeReplaced()
    {
        using AgentReleaseApplication configured = new();
        SignedInClient administrator = await configured.AdministratorAsync();

        HttpResponseMessage response = await UploadAsync(administrator, LinuxExecutables.Versioned("26.1.512"), await administrator.TokenAsync());

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(AgentBinarySource.Configuration, (await RegisteredMachine.ReadAsync<AgentBinaryView>(await administrator.GetAsync(Settings))).Source);
    }

    private static async Task<HttpResponseMessage> UploadAsync(SignedInClient client, byte[] content, string? token)
    {
        using HttpRequestMessage request = new(HttpMethod.Put, new Uri(Settings + "/binary", UriKind.Relative))
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
