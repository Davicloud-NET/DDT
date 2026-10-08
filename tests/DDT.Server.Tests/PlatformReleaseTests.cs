// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using DDT.Contracts.Settings;
using DDT.Server.Settings;
using Xunit;

namespace DDT.Server.Tests;

// The agent of each platform, as an agent asks for the one built for its own system. A release puts the one for
// Linux next to the server too, and an upload wins over it.
public sealed class PlatformReleaseTests
{
    // The paths and property names are literal on purpose. Agents in boot environments built long ago ask for these.
    private const string Linux = "api/agents/releases/linux-x64";
    private const string Windows = "api/agents/releases/windows-x64";
    private const string Frozen = "api/agents/release";
    private const string LinuxSettings = "/api/settings/agent/linux";

    [Fact]
    public async Task ANewServerOffersTheAgentForLinuxItCameWith()
    {
        using BundledReleaseApplication application = new();
        byte[] bundled = LinuxExecutables.Versioned("26.1.512");
        await File.WriteAllBytesAsync(Path.Combine(application.Bundled.Folder, "ddt-agent-linux-x64"), bundled, TestContext.Current.CancellationToken);
        SignedInClient administrator = await application.AdministratorAsync();
        using AgentClient agent = new(application.CreateDefaultClient(), TestRemoteAddress.Unique());

        AgentBinaryView view = await RegisteredMachine.ReadAsync<AgentBinaryView>(await administrator.GetAsync(LinuxSettings));

        Assert.Equal((AgentBinarySource.Bundled, "26.1.512", ConsolePackages.Sha256(bundled)), (view.Source, view.Version, view.Sha256));
        Assert.Null(view.UploadedBy);
        await AssertReleaseAsync(agent, Linux, bundled);

        // The server came with no agent for Windows PE, and the one for Linux is not handed out as one.
        Assert.Equal(HttpStatusCode.NotFound, (await agent.GetAsync(Frozen)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await agent.GetAsync(Frozen + "/binary")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await agent.GetAsync(Windows)).StatusCode);
    }

    [Fact]
    public async Task AnUploadWinsUntilItIsRemovedAndTheServersNewerAgentIsNamed()
    {
        using BundledReleaseApplication application = new();
        byte[] bundled = LinuxExecutables.Versioned("26.1.512");
        await File.WriteAllBytesAsync(application.Bundled.LinuxAgentPath, bundled, TestContext.Current.CancellationToken);
        SignedInClient administrator = await application.AdministratorAsync();
        using AgentClient agent = new(application.CreateDefaultClient(), TestRemoteAddress.Unique());
        byte[] older = LinuxExecutables.Versioned("26.1.400");

        AgentBinaryView uploaded = await RegisteredMachine.ReadAsync<AgentBinaryView>(await UploadAsync(administrator, older));

        Assert.Equal((AgentBinarySource.Uploaded, "26.1.400", "26.1.512"), (uploaded.Source, uploaded.Version, uploaded.NewerBundledVersion));
        await AssertReleaseAsync(agent, Linux, older);

        // An agent that carries no version is nothing to compare.
        byte[] unmarked = [.. LinuxExecutables.Header(), .. RandomNumberGenerator.GetBytes(2048)];
        AgentBinaryView without = await RegisteredMachine.ReadAsync<AgentBinaryView>(await UploadAsync(administrator, unmarked));
        Assert.Equal((AgentBinarySource.Uploaded, null, null), (without.Source, without.Version, without.NewerBundledVersion));

        AgentBinaryView removed = await RegisteredMachine.ReadAsync<AgentBinaryView>(await administrator.DeleteAsync(LinuxSettings + "/binary"));

        Assert.Equal((AgentBinarySource.Bundled, "26.1.512"), (removed.Source, removed.Version));
        await AssertReleaseAsync(agent, Linux, bundled);
    }

    [Fact]
    public async Task TheAgentForWindowsPEIsAnsweredUnderItsPlatformToo()
    {
        using BundledReleaseApplication application = new();
        byte[] bundled = Executables.Versioned("0.4.0");
        await File.WriteAllBytesAsync(application.Bundled.AgentPath, bundled, TestContext.Current.CancellationToken);
        using AgentClient agent = new(application.CreateDefaultClient(), TestRemoteAddress.Unique());

        await AssertReleaseAsync(agent, Windows, bundled);
        await AssertReleaseAsync(agent, Frozen, bundled);
        Assert.Equal(HttpStatusCode.NotFound, (await agent.GetAsync(Linux)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await agent.GetAsync(Linux + "/binary")).StatusCode);
    }

    [Theory]
    [InlineData("linux-arm64")]
    [InlineData("LINUX-X64")]
    [InlineData("console")]
    [InlineData("linux-x64.exe")]
    [InlineData("linux-x64%2Fbinary")]
    public async Task APlatformNoAgentIsBuiltForHasNone(string platform)
    {
        using BundledReleaseApplication application = new();
        await File.WriteAllBytesAsync(application.Bundled.LinuxAgentPath, LinuxExecutables.Versioned("26.1.512"), TestContext.Current.CancellationToken);
        using AgentClient agent = new(application.CreateDefaultClient(), TestRemoteAddress.Unique());

        Assert.Equal(HttpStatusCode.NotFound, (await agent.GetAsync($"api/agents/releases/{platform}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await agent.GetAsync($"api/agents/releases/{platform}/binary")).StatusCode);
    }

    // A file configuration names is served as it is, like the one for Windows PE, and noticed when it is replaced.
    [Fact]
    public async Task ServesTheConfiguredAgentForLinuxAndNoticesWhenItIsReplaced()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        using AgentReleaseApplication application = new();
        using AgentClient agent = new(application.CreateDefaultClient(), TestRemoteAddress.Unique());

        Assert.Equal(HttpStatusCode.NotFound, (await agent.GetAsync(Linux)).StatusCode);

        byte[] first = LinuxExecutables.Versioned("26.1.511");
        await File.WriteAllBytesAsync(application.LinuxBinaryPath, first, cancellationToken);
        await AssertReleaseAsync(agent, Linux, first);

        byte[] second = LinuxExecutables.Versioned("26.1.512");
        DateTime written = File.GetLastWriteTimeUtc(application.LinuxBinaryPath);
        await File.WriteAllBytesAsync(application.LinuxBinaryPath, second, cancellationToken);
        File.SetLastWriteTimeUtc(application.LinuxBinaryPath, written.AddSeconds(10));
        await AssertReleaseAsync(agent, Linux, second);
    }

    private static async Task AssertReleaseAsync(AgentClient agent, string route, byte[] content)
    {
        HttpResponseMessage answer = await agent.GetAsync(route);
        Assert.Equal(HttpStatusCode.OK, answer.StatusCode);
        using JsonDocument release = JsonDocument.Parse(await answer.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

        Assert.Equal(["sha256", "size"], release.RootElement.EnumerateObject().Select(property => property.Name));
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(content)), release.RootElement.GetProperty("sha256").GetString());
        Assert.Equal(content.Length, release.RootElement.GetProperty("size").GetInt64());

        HttpResponseMessage binary = await agent.GetAsync(route + "/binary");
        Assert.Equal(HttpStatusCode.OK, binary.StatusCode);
        Assert.Equal(content, await binary.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken));
    }

    private static async Task<HttpResponseMessage> UploadAsync(SignedInClient client, byte[] content)
    {
        using HttpRequestMessage request = new(HttpMethod.Put, new Uri(LinuxSettings + "/binary", UriKind.Relative))
        {
            Content = new ByteArrayContent(content) { Headers = { ContentType = new MediaTypeHeaderValue("application/octet-stream") } },
        };
        request.Headers.Add(ReauthenticationTokens.HeaderName, await client.TokenAsync());

        return await client.SendAsync(request, TestContext.Current.CancellationToken);
    }
}
