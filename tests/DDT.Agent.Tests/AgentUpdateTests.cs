// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.ComponentModel;
using System.Net;
using System.Net.Sockets;
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

    // As in Windows PE right after a restart, before the network is up: the connection is not accepted in time. A
    // listener that never accepts leaves the TLS handshake unanswered, which the connect timeout covers.
    [Fact]
    public async Task SaysThatTheServerDidNotAcceptTheConnectionInTime()
    {
        using TcpListener listener = new(IPAddress.Loopback, 0);
        listener.Start();

        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        using HttpAgentServer server = new(new Uri($"https://127.0.0.1:{port}/"), null, connectTimeout: TimeSpan.FromMilliseconds(300));
        ManualTimeProvider time = new();
        using StringWriter console = new();
        AgentUpdate update = new(server, new ScriptedRelauncher(() => 0), new AgentLog(time, console), time, CurrentSha256, _directory, []);
        using CancellationTokenSource stop = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        using CancellationTokenSource limit = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        limit.CancelAfter(TimeSpan.FromSeconds(10));

        Task<int?> running = update.RunAsync(stop.Token);

        // The retry's delay only runs out when the test moves the time, so the agent waits there after one attempt,
        // unless it gave up at once.
        while (time.PendingTimers == 0 && !running.IsCompleted)
        {
            await Task.Delay(10, limit.Token);
        }

        await stop.CancelAsync();

        Assert.Null(await running);
        Assert.Contains(
            $"WARN  Cannot reach the server to ask for the current agent (the server at 127.0.0.1:{port} did not accept a connection within 0.3 s).",
            console.ToString(),
            StringComparison.Ordinal);
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
