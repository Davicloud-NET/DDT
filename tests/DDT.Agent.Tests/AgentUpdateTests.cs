// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.ComponentModel;
using System.Net;
using System.Security.Cryptography;
using DDT.Contracts.Agents;
using Xunit;

namespace DDT.Agent.Tests;

public sealed class AgentUpdateTests : IDisposable
{
    private const string CurrentSha256 = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

    private readonly string _directory = Directory.CreateTempSubdirectory("ddt-agent-update-").FullName;

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private static AgentRelease ReleaseOf(byte[] content) =>
        new(Convert.ToHexStringLower(SHA256.HashData(content)), content.Length);

    private (AgentUpdate Update, ImmediateTimeProvider Time) Create(ScriptedAgentServer server, IAgentRelauncher relauncher, params string[] arguments)
    {
        ImmediateTimeProvider time = new();

        return (new AgentUpdate(server, relauncher, new AgentLog(time, TextWriter.Null), time, CurrentSha256, _directory, arguments), time);
    }

    [Fact]
    public async Task CarriesOnWhenTheServerOffersNoAgentOrThisOne()
    {
        ScriptedAgentServer server = new ScriptedAgentServer()
            .OnRelease(() => null)
            .OnRelease(() => new AgentRelease(CurrentSha256.ToUpperInvariant(), 10));
        ScriptedRelauncher relauncher = new(() => 0);
        (AgentUpdate update, _) = Create(server, relauncher);

        Assert.Null(await update.RunAsync(TestContext.Current.CancellationToken));
        Assert.Null(await update.RunAsync(TestContext.Current.CancellationToken));
        Assert.Empty(relauncher.Started);
        Assert.DoesNotContain("download", server.Calls);
    }

    [Fact]
    public async Task RunsTheNewAgentAndEndsWithItsExitCode()
    {
        byte[] content = RandomNumberGenerator.GetBytes(3000);
        AgentRelease release = ReleaseOf(content);
        ScriptedAgentServer server = new ScriptedAgentServer().OnRelease(() => release).OnDownload(content);
        ScriptedRelauncher relauncher = new(() => AgentExitCodes.Rejected);
        (AgentUpdate update, _) = Create(server, relauncher, "--config", "agent.json");

        Assert.Equal(AgentExitCodes.Rejected, await update.RunAsync(TestContext.Current.CancellationToken));

        (string path, IReadOnlyList<string> arguments) = Assert.Single(relauncher.Started);
        Assert.Equal(Path.Combine(_directory, $"ddt-agent-{release.Sha256[..12]}.exe"), path);
        Assert.Equal(content, await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken));
        Assert.Equal(["--config", "agent.json", AgentOptions.NoUpdateArgument], arguments);
    }

    [Fact]
    public async Task NeverStartsADownloadThatDoesNotMatch()
    {
        byte[] content = RandomNumberGenerator.GetBytes(3000);
        ScriptedAgentServer server = new ScriptedAgentServer()
            .OnRelease(() => ReleaseOf(content))
            .OnDownload(content[..2000]);
        ScriptedRelauncher relauncher = new(() => 0);
        (AgentUpdate update, _) = Create(server, relauncher);

        Assert.Null(await update.RunAsync(TestContext.Current.CancellationToken));
        Assert.Empty(relauncher.Started);
        Assert.Empty(Directory.GetFiles(_directory));
    }

    [Fact]
    public async Task NeverStartsADownloadOfTheRightSizeWithOtherContent()
    {
        byte[] content = RandomNumberGenerator.GetBytes(3000);
        byte[] replaced = [.. content];
        replaced[1500] ^= 0xFF;
        ScriptedAgentServer server = new ScriptedAgentServer().OnRelease(() => ReleaseOf(content)).OnDownload(replaced);
        ScriptedRelauncher relauncher = new(() => 0);
        (AgentUpdate update, _) = Create(server, relauncher);

        Assert.Null(await update.RunAsync(TestContext.Current.CancellationToken));
        Assert.Contains("download", server.Calls);
        Assert.Empty(relauncher.Started);
        Assert.Empty(Directory.GetFiles(_directory));
    }

    [Fact]
    public async Task ReusesAnEarlierDownloadOnlyWhenItMatches()
    {
        byte[] content = RandomNumberGenerator.GetBytes(3000);
        AgentRelease release = ReleaseOf(content);
        string path = Path.Combine(_directory, $"ddt-agent-{release.Sha256[..12]}.exe");
        await File.WriteAllBytesAsync(path, RandomNumberGenerator.GetBytes(3000), TestContext.Current.CancellationToken);

        ScriptedAgentServer server = new ScriptedAgentServer()
            .OnRelease(() => release)
            .OnDownload(content)
            .OnRelease(() => release);
        (AgentUpdate update, _) = Create(server, new ScriptedRelauncher(() => 0));

        Assert.Equal(0, await update.RunAsync(TestContext.Current.CancellationToken));
        Assert.Equal(content, await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken));

        Assert.Equal(0, await update.RunAsync(TestContext.Current.CancellationToken));
        Assert.Single(server.Calls, call => call == "download");
    }

    [Theory]
    [InlineData(AgentExitCodes.ConfigurationError)]
    [InlineData(unchecked((int)0xC0000135))]
    [InlineData(64)]
    public async Task CarriesOnWhenTheNewAgentCannotRun(int exitCode)
    {
        byte[] content = RandomNumberGenerator.GetBytes(3000);
        ScriptedAgentServer server = new ScriptedAgentServer().OnRelease(() => ReleaseOf(content)).OnDownload(content);
        (AgentUpdate update, _) = Create(server, new ScriptedRelauncher(() => exitCode));

        Assert.Null(await update.RunAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task CarriesOnWhenTheNewAgentDoesNotStart()
    {
        byte[] content = RandomNumberGenerator.GetBytes(3000);
        ScriptedAgentServer server = new ScriptedAgentServer().OnRelease(() => ReleaseOf(content)).OnDownload(content);
        (AgentUpdate update, _) = Create(server, new ScriptedRelauncher(() => throw new Win32Exception(193)));

        Assert.Null(await update.RunAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task WaitsForTheServerButNotForeverForABusyOne()
    {
        ScriptedAgentServer unreachable = new ScriptedAgentServer()
            .OnRelease(() => throw new HttpRequestException("No such host is known."))
            .OnRelease(() => throw new HttpRequestException("No such host is known."))
            .OnRelease(() => null);
        (AgentUpdate first, ImmediateTimeProvider time) = Create(unreachable, new ScriptedRelauncher(() => 0));

        Assert.Null(await first.RunAsync(TestContext.Current.CancellationToken));
        Assert.Equal(3, unreachable.Calls.Count(call => call == "release"));
        Assert.Equal([TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(4)], time.Delays);

        ScriptedAgentServer busy = new();

        for (int attempt = 0; attempt < 10; attempt++)
        {
            busy.OnRelease(() => throw new HttpRequestException("Too many requests.", null, HttpStatusCode.TooManyRequests));
        }

        (AgentUpdate second, ImmediateTimeProvider busyTime) = Create(busy, new ScriptedRelauncher(() => 0));

        Assert.Null(await second.RunAsync(TestContext.Current.CancellationToken));
        Assert.Equal(7, busy.Calls.Count(call => call == "release"));

        // Outlasts the server's one minute window, so a lab that asked all at once gets its turn.
        Assert.True(busyTime.Delays.Aggregate(TimeSpan.Zero, (total, delay) => total + delay) > TimeSpan.FromMinutes(1));
    }
}
