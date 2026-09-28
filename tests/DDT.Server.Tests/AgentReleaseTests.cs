// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using Xunit;

namespace DDT.Server.Tests;

public sealed class AgentReleaseTests(AgentReleaseApplication application) : IClassFixture<AgentReleaseApplication>
{
    // The paths and property names are literal on purpose. Agents in boot images built long ago ask for exactly these.
    private const string Release = "api/agents/release";
    private const string ReleaseBinary = "api/agents/release/binary";
    private const string ConsoleRelease = "api/agents/release/console";

    [Fact]
    public async Task ServesTheConfiguredAgentAndNoticesWhenItIsReplaced()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        using AgentClient agent = new(application.CreateDefaultClient(), TestRemoteAddress.Unique());

        File.Delete(application.BinaryPath);

        Assert.Equal(HttpStatusCode.NotFound, (await agent.GetAsync(Release)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await agent.GetAsync(ReleaseBinary)).StatusCode);

        byte[] first = RandomNumberGenerator.GetBytes(4096);
        await File.WriteAllBytesAsync(application.BinaryPath, first, cancellationToken);
        await AssertReleaseAsync(agent, first);

        HttpResponseMessage binary = await agent.GetAsync(ReleaseBinary);
        Assert.Equal(first, await binary.Content.ReadAsByteArrayAsync(cancellationToken));

        byte[] second = RandomNumberGenerator.GetBytes(5000);
        await File.WriteAllBytesAsync(application.BinaryPath, second, cancellationToken);
        await AssertReleaseAsync(agent, second);

        // A rebuilt agent often has exactly the same size, because the linker pads it.
        byte[] third = RandomNumberGenerator.GetBytes(5000);
        DateTime written = File.GetLastWriteTimeUtc(application.BinaryPath);
        await File.WriteAllBytesAsync(application.BinaryPath, third, cancellationToken);
        File.SetLastWriteTimeUtc(application.BinaryPath, written.AddSeconds(10));
        await AssertReleaseAsync(agent, third);
    }

    // A zip that isn't a complete console is treated like no zip at all.
    // Machines keep the console from their boot image.
    [Fact]
    public async Task ServesTheConfiguredConsoleFileByFile()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        using AgentClient agent = new(application.CreateDefaultClient(), TestRemoteAddress.Unique());

        File.Delete(application.ConsolePath);
        Assert.Equal(HttpStatusCode.NotFound, (await agent.GetAsync(ConsoleRelease)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await agent.GetAsync($"{ConsoleRelease}/ddt-console.exe")).StatusCode);

        (string Path, byte[] Content)[] files = ConsolePackages.Files();
        await File.WriteAllBytesAsync(application.ConsolePath, ConsolePackages.Zip(files[..2]), cancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, (await agent.GetAsync(ConsoleRelease)).StatusCode);

        await File.WriteAllBytesAsync(application.ConsolePath, ConsolePackages.Zip(files), cancellationToken);
        File.SetLastWriteTimeUtc(application.ConsolePath, DateTime.UtcNow.AddSeconds(10));

        using JsonDocument release = await ReadJsonAsync(await agent.GetAsync(ConsoleRelease));
        JsonElement[] released = [.. release.RootElement.GetProperty("files").EnumerateArray()];

        Assert.Equal(["ddt-console.exe", "libSkiaSharp.dll", "libHarfBuzzSharp.dll"], released.Select(file => file.GetProperty("name").GetString()));
        Assert.Equal(files.Select(file => ConsolePackages.Sha256(file.Content)), released.Select(file => file.GetProperty("sha256").GetString()));
        Assert.Equal(files.Select(file => (long)file.Content.Length), released.Select(file => file.GetProperty("size").GetInt64()));

        HttpResponseMessage library = await agent.GetAsync($"{ConsoleRelease}/libSkiaSharp.dll");
        Assert.Equal(files[1].Content, await library.Content.ReadAsByteArrayAsync(cancellationToken));
    }

    private static async Task AssertReleaseAsync(AgentClient agent, byte[] content)
    {
        using JsonDocument release = await ReadJsonAsync(await agent.GetAsync(Release));

        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(content)), release.RootElement.GetProperty("sha256").GetString());
        Assert.Equal(content.Length, release.RootElement.GetProperty("size").GetInt64());
    }

    private static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return JsonDocument.Parse(await response.Content.ReadAsStringAsync());
    }
}
