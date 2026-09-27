// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.ComponentModel;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using DDT.ConsoleProtocol;
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

    private string AgentPath => Path.Combine(_directory, "ddt-agent.exe");

    // As a boot image has it: the console beside the agent, with contents of its own.
    private string BootConsole(IReadOnlyList<byte[]> contents)
    {
        foreach ((string name, byte[] content) in ConsolePipe.Files.Zip(contents))
        {
            File.WriteAllBytes(Path.Combine(_directory, name), content);
        }

        return Path.Combine(_directory, ConsolePipe.FileName);
    }

    private static byte[][] ConsoleContents() => [.. ConsolePipe.Files.Select(_ => RandomNumberGenerator.GetBytes(2000))];

    private static ConsoleRelease ConsoleOf(IReadOnlyList<byte[]> contents) =>
        new([.. ConsolePipe.Files.Zip(contents, (name, content) => new ConsoleReleaseFile(name, Convert.ToHexStringLower(SHA256.HashData(content)), content.Length))]);

    private static ScriptedAgentServer Offering(IReadOnlyList<byte[]> console, Func<AgentRelease?>? agent = null)
    {
        ScriptedAgentServer server = new ScriptedAgentServer()
            .OnRelease(agent ?? (() => new AgentRelease(CurrentSha256, 10)))
            .OnConsoleRelease(() => ConsoleOf(console));

        foreach ((string name, byte[] content) in ConsolePipe.Files.Zip(console))
        {
            server.WithConsoleFile(name, content);
        }

        return server;
    }

    private AgentUpdate WithConsole(ScriptedAgentServer server, IAgentRelauncher relauncher, string consolePath, params string[] arguments)
    {
        ImmediateTimeProvider time = new();

        return new AgentUpdate(
            server,
            relauncher,
            new AgentLog(time, TextWriter.Null),
            time,
            CurrentSha256,
            _directory,
            arguments,
            consolePath: consolePath,
            agentPath: AgentPath);
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

    // The update check is the first call to the server, so the console at the machine shows why it does not get through.
    [Fact]
    public async Task ShowsWhyTheServerCannotBeReachedUntilItAnswers()
    {
        ScriptedAgentServer server = new ScriptedAgentServer()
            .OnRelease(() => throw new HttpRequestException(HttpRequestError.NameResolutionError, "the name ddt.example cannot be found in DNS"))
            .OnRelease(() => null);
        ScriptedMachineConsole console = new();
        ImmediateTimeProvider time = new();
        AgentUpdate update = new(
            server,
            new ScriptedRelauncher(() => 0),
            new AgentLog(time, TextWriter.Null),
            time,
            CurrentSha256,
            _directory,
            [],
            TestAgents.Status(console));

        Assert.Null(await update.RunAsync(TestContext.Current.CancellationToken));

        Assert.Equal(
            [
                new ConsoleServer("https://ddt.example:8443/"),
                new ConsoleServer("https://ddt.example:8443/", "the name ddt.example cannot be found in DNS", ConnectionStage.NameLookup, 1),
                new ConsoleServer("https://ddt.example:8443/"),
            ],
            console.States.Select(state => state.Server));
        Assert.All(console.States, state => Assert.Equal(ConsoleStage.Starting, state.Stage));
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
            $"WARN  Cannot reach the server to ask for the current agent (the server at 127.0.0.1:{port} accepted a connection at 127.0.0.1:{port} after ",
            console.ToString(),
            StringComparison.Ordinal);
        Assert.Contains("but the TLS handshake did not finish within 0.3 s).", console.ToString(), StringComparison.Ordinal);
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

    // The server takes a console of exactly these files and names them in this order.
    [Fact]
    public void TheConsoleIsMadeOfTheFilesTheServerTakes()
    {
        Assert.Equal(ConsoleRelease.FileNames, ConsolePipe.Files);
    }

    // A newer console alone starts this agent again, with the console downloaded next to it.
    [Fact]
    public async Task StartsAgainWithTheConsoleTheServerOffers()
    {
        byte[][] offered = ConsoleContents();
        ScriptedAgentServer server = Offering(offered);
        ScriptedRelauncher relauncher = new(() => AgentExitCodes.Rejected);
        AgentUpdate update = WithConsole(server, relauncher, BootConsole(ConsoleContents()), "--config", "agent.json");

        Assert.Equal(AgentExitCodes.Rejected, await update.RunAsync(TestContext.Current.CancellationToken));

        string folder = Path.Combine(_directory, $"console-{ConsoleOf(offered).Files[0].Sha256[..12]}");
        (string path, IReadOnlyList<string> arguments) = Assert.Single(relauncher.Started);
        Assert.Equal(AgentPath, path);
        Assert.Equal(["--config", "agent.json", AgentOptions.ConsoleArgument, Path.Combine(folder, ConsolePipe.FileName), AgentOptions.NoUpdateArgument], arguments);

        foreach ((string name, byte[] content) in ConsolePipe.Files.Zip(offered))
        {
            Assert.Equal(content, await File.ReadAllBytesAsync(Path.Combine(folder, name), TestContext.Current.CancellationToken));
        }
    }

    [Fact]
    public async Task SwitchesAgentAndConsoleInOneStart()
    {
        byte[] agent = RandomNumberGenerator.GetBytes(3000);
        ScriptedAgentServer server = Offering(ConsoleContents(), () => ReleaseOf(agent)).OnDownload(agent);
        ScriptedRelauncher relauncher = new(() => 0);
        AgentUpdate update = WithConsole(server, relauncher, BootConsole(ConsoleContents()));

        Assert.Equal(0, await update.RunAsync(TestContext.Current.CancellationToken));

        (string path, IReadOnlyList<string> arguments) = Assert.Single(relauncher.Started);
        Assert.Equal(Path.Combine(_directory, $"ddt-agent-{ReleaseOf(agent).Sha256[..12]}.exe"), path);
        Assert.Equal(AgentOptions.ConsoleArgument, arguments[0]);
        Assert.Equal(AgentOptions.NoUpdateArgument, arguments[^1]);
    }

    [Fact]
    public async Task KeepsTheConsoleWhenTheServerOffersTheSameOrNone()
    {
        byte[][] current = ConsoleContents();
        string console = BootConsole(current);
        ScriptedRelauncher relauncher = new(() => 0);

        ScriptedAgentServer same = Offering(current);
        Assert.Null(await WithConsole(same, relauncher, console).RunAsync(TestContext.Current.CancellationToken));
        Assert.DoesNotContain(same.Calls, call => call.StartsWith("console-download", StringComparison.Ordinal));

        ScriptedAgentServer none = new ScriptedAgentServer().OnRelease(() => null);
        Assert.Null(await WithConsole(none, relauncher, console).RunAsync(TestContext.Current.CancellationToken));
        Assert.Contains("console-release", none.Calls);

        Assert.Empty(relauncher.Started);
    }

    // A console named on the command line is the one someone wanted.
    [Fact]
    public async Task KeepsAConsoleNamedOnTheCommandLine()
    {
        ScriptedAgentServer server = Offering(ConsoleContents());
        (AgentUpdate update, _) = Create(server, new ScriptedRelauncher(() => 0));

        Assert.Null(await update.RunAsync(TestContext.Current.CancellationToken));
        Assert.DoesNotContain("console-release", server.Calls);
    }

    [Fact]
    public async Task NeverStartsAConsoleThatDoesNotMatch()
    {
        byte[][] offered = ConsoleContents();
        ScriptedAgentServer server = Offering(offered).WithConsoleFile(ConsolePipe.Files[1], RandomNumberGenerator.GetBytes(2000));
        ScriptedRelauncher relauncher = new(() => 0);
        AgentUpdate update = WithConsole(server, relauncher, BootConsole(ConsoleContents()));

        Assert.Null(await update.RunAsync(TestContext.Current.CancellationToken));
        Assert.Empty(relauncher.Started);
        Assert.Empty(Directory.GetFiles(_directory, "*.part", SearchOption.AllDirectories));
    }

    // The names become paths next to the agent, so a release that names anything else is not downloaded at all.
    [Fact]
    public async Task DownloadsOnlyTheConsolesOwnFiles()
    {
        ConsoleRelease release = ConsoleOf(ConsoleContents());
        ScriptedAgentServer server = new ScriptedAgentServer()
            .OnRelease(() => new AgentRelease(CurrentSha256, 10))
            .OnConsoleRelease(() => release with { Files = [release.Files[0], release.Files[1], release.Files[2] with { Name = @"..\startnet.cmd" }] });
        ScriptedRelauncher relauncher = new(() => 0);
        AgentUpdate update = WithConsole(server, relauncher, BootConsole(ConsoleContents()));

        Assert.Null(await update.RunAsync(TestContext.Current.CancellationToken));
        Assert.DoesNotContain(server.Calls, call => call.StartsWith("console-download", StringComparison.Ordinal));
        Assert.Empty(relauncher.Started);
    }
}
