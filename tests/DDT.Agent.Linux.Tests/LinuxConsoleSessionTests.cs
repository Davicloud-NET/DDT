// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.IO.Pipes;
using System.Security.Cryptography;
using DDT.Agent.Consoles;
using DDT.Agent.LinuxPhase;
using DDT.ConsoleProtocol;
using DDT.Contracts.Agents;
using Xunit;

namespace DDT.Agent.Tests;

// The console in the installed Linux, over a folder that stands in for /. systemd is stood in for; the socket is real.
public sealed class LinuxConsoleSessionTests : IAsyncDisposable
{
    private const UnixFileMode Readable = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.OtherRead;

    private const UnixFileMode Executable = Readable | UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute;

    private const UnixFileMode Passable = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
        | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute;

    private readonly string _folder = Directory.CreateTempSubdirectory("ddt-linux-console-").FullName;
    private readonly StringWriter _writer = new();
    private readonly AgentLog _log;
    private readonly RecordingUnit _unit = new();
    private readonly byte[][] _console = [.. ConsolePipe.LinuxFiles.Select(_ => RandomNumberGenerator.GetBytes(1000))];
    private LinuxConsoleSession? _session;

    public LinuxConsoleSessionTests() => _log = new AgentLog(new ImmediateTimeProvider(), _writer);

    private LinuxAgentPaths Paths => new(Path.Combine(_folder, "system"));

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    public async ValueTask DisposeAsync()
    {
        if (_session is not null)
        {
            await _session.DisposeAsync();
        }

        _writer.Dispose();
        Directory.Delete(_folder, recursive: true);
    }

    [Fact]
    public void TheUnitRunsTheConsoleAsAUserOfItsOwnOnItsTerminal()
    {
        IReadOnlyList<string> arguments = LinuxConsoleUnit.Arguments("/usr/local/lib/ddt/console-1/ddt-console", "/run/ddt-console/ddt-console-1", 8);

        Assert.Equal("--unit=ddt-console.service", arguments[0]);
        Assert.Contains("--property=DynamicUser=yes", arguments);
        Assert.Contains("--property=SupplementaryGroups=video input", arguments);
        Assert.Contains("--property=StandardInput=tty", arguments);
        Assert.Contains("--property=TTYPath=/dev/tty8", arguments);
        Assert.Contains("--property=PrivateNetwork=yes", arguments);
        Assert.Contains("--property=CapabilityBoundingSet=", arguments);
        Assert.Contains("--property=RestartPreventExitStatus=2 3 5", arguments);
        Assert.Equal(["--", "/usr/local/lib/ddt/console-1/ddt-console", "--pipe", "/run/ddt-console/ddt-console-1", "--session"], arguments.TakeLast(5));
    }

    [Theory]
    [InlineData("0::/system.slice/ddt-console.service", true)]
    [InlineData("12:pids:/system.slice/ddt-console.service\n1:name=systemd:/system.slice/ddt-console.service", true)]
    [InlineData("0::/system.slice/ddt-agent.service", false)]
    [InlineData("0::/user.slice/user-1000.slice/session-2.scope", false)]
    [InlineData("0::/system.slice/ddt-console.service/inner", false)]
    [InlineData("", false)]
    public void OnlyAProcessOfTheConsolesUnitIsLetIn(string groups, bool letIn) =>
        Assert.Equal(letIn, ConsolePeer.InUnit(groups.Split('\n')));

    [Theory]
    [InlineData("/etc/systemd/system/display-manager.service", false, "A display manager has this machine's screen")]
    [InlineData("/dev/fb0", true, "This machine has no framebuffer, /dev/fb0")]
    [InlineData("/dev/tty0", true, "This Linux has no virtual terminals")]
    [InlineData("/lib64/ld-linux-x86-64.so.2", true, "This Linux has no glibc, which the console is built for")]
    public void TheScreenSaysWhyThereIsNoPicture(string file, bool remove, string problem)
    {
        Screen();
        string path = Paths.At(file);

        if (remove)
        {
            File.Delete(path);
        }
        else
        {
            Put(path);
        }

        Assert.Equal(problem, LinuxConsoleScreen.Problem(Paths));
    }

    [Fact]
    public async Task WithoutAScreenNothingIsFetchedOrStarted()
    {
        ScriptedAgentServer server = Offering();
        LinuxConsoleSession session = Session(server);

        await session.PrepareAsync(Cancellation);

        Assert.Equal(["stop"], _unit.Calls);
        Assert.DoesNotContain("console-release", server.Calls);
        Assert.Contains("INFO  This machine has no framebuffer, /dev/fb0, so the run is not shown on this machine's screen.", Lines());
        Assert.True(await session.EndAsync(signOut: false, Cancellation));
    }

    [Fact]
    public async Task WithoutAConsoleFromTheServerNothingIsStarted()
    {
        Screen();
        LinuxConsoleSession session = Session(new ScriptedAgentServer().OnConsoleRelease(() => null));

        await session.PrepareAsync(Cancellation);

        Assert.Equal(["stop"], _unit.Calls);
        Assert.Contains("INFO  The server has no console for Linux, so the run is not shown on this machine's screen.", Lines());
    }

    [Fact]
    public async Task TheServersConsoleStartsOnAFreeTerminalWithASocketOnlyItsUnitIsLetInto()
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(), "The socket and the file modes are Linux's.");
        Screen();
        LinuxConsoleSession session = Session(Offering());

        await session.PrepareAsync(Cancellation);

        Assert.Equal(["stop", "start 8"], _unit.Calls);
        string program = _unit.Program!;
        string folder = Path.GetDirectoryName(program)!;
        Assert.Equal(Paths.Program, Path.GetDirectoryName(folder));
        Assert.Equal(Executable, Mode(folder));
        Assert.Equal(Executable, Mode(program));
        Assert.Equal(Readable, Mode(Path.Combine(folder, "libSkiaSharp.so")));
        string socket = _unit.Socket!;
        Assert.Equal(Paths.ConsoleSockets, Path.GetDirectoryName(socket));
        Assert.Equal(socket, ConsolePipe.NameFrom(["--pipe", socket]));

        // This test's process is no process of the console's unit.
        await using (NamedPipeClientStream client = new(".", socket, PipeDirection.InOut, PipeOptions.Asynchronous))
        {
            await client.ConnectAsync(TimeSpan.FromSeconds(10), Cancellation);
            Assert.Equal(0, await client.ReadAsync(new byte[16], Cancellation));
        }

        Assert.Equal(Passable, Mode(Paths.ConsoleSockets));
        Assert.Contains(Lines(), line => line.StartsWith("WARN  Process ", StringComparison.Ordinal)
            && line.EndsWith(" connected to the console's socket, but it is not the console's. It was let go.", StringComparison.Ordinal));
        Assert.Contains("INFO  The console shows the run on terminal 8.", Lines());

        await session.DisposeAsync();
        _session = null;
        Assert.False(Directory.Exists(Paths.ConsoleSockets));
    }

    [Fact]
    public async Task AConsoleSystemdDidNotStartIsNotWaitedFor()
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(), "The socket is Linux's.");
        Screen();
        _unit.Refusal = "Unit ddt-console.service was already loaded";
        LinuxConsoleSession session = Session(Offering());

        await session.PrepareAsync(Cancellation);

        Assert.Contains("WARN  systemd did not start the console (Unit ddt-console.service was already loaded), so the run is not shown on this machine's screen.", Lines());
        Assert.True(await session.EndAsync(signOut: false, Cancellation));
        Assert.Equal(["stop", "start 8"], _unit.Calls);
    }

    [Fact]
    public async Task AFinishedRunStopsTheConsoleAndAFailedOneWaitsForF9()
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(), "The socket is Linux's.");
        Screen();
        LinuxConsoleSession session = Session(Offering());
        await session.PrepareAsync(Cancellation);

        Assert.True(await session.EndAsync(signOut: false, Cancellation));
        Assert.True(await session.EndAsync(signOut: true, Cancellation));

        Assert.Equal(["stop", "start 8", "wait", "stop"], _unit.Calls);
        Assert.Contains("INFO  The console shows how the run ended until F9 is pressed at the machine.", Lines());
    }

    private LinuxConsoleSession Session(IAgentServer server)
    {
        SessionMachineConsole console = new(_log, "1.0.0") { Name = "the console" };
        _session = new LinuxConsoleSession(console, server, _unit, Paths, _log);

        return _session;
    }

    // What the console needs of the system, and no display manager.
    private void Screen()
    {
        foreach (string file in (string[])["/dev/fb0", "/dev/tty0", "/lib64/ld-linux-x86-64.so.2"])
        {
            Put(Paths.At(file));
        }

        Directory.CreateDirectory(Paths.Program);
    }

    private static UnixFileMode Mode(string path) => OperatingSystem.IsWindows() ? UnixFileMode.None : File.GetUnixFileMode(path);

    private static void Put(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "");
    }

    private ScriptedAgentServer Offering()
    {
        ScriptedAgentServer server = new ScriptedAgentServer().OnConsoleRelease(() => new ConsoleRelease(
            [.. ConsolePipe.LinuxFiles.Zip(_console, (name, content) => new ConsoleReleaseFile(name, Convert.ToHexStringLower(SHA256.HashData(content)), content.Length))]));

        foreach ((string name, byte[] content) in ConsolePipe.LinuxFiles.Zip(_console))
        {
            server.WithConsoleFile(name, content);
        }

        return server;
    }

    private string[] Lines() =>
        [.. _writer.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries).Select(line => line[9..])];

    private sealed class RecordingUnit : IConsoleUnit
    {
        public List<string> Calls { get; } = [];

        public string? Program { get; private set; }

        public string? Socket { get; private set; }

        public string? Refusal { get; set; }

        public int FreeTerminal() => 8;

        public Task<string?> StartAsync(string program, string socket, int terminal, CancellationToken cancellationToken)
        {
            Calls.Add($"start {terminal}");
            Program = program;
            Socket = socket;

            return Task.FromResult(Refusal);
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            Calls.Add("stop");

            return Task.CompletedTask;
        }

        public Task<bool> WaitUntilGoneAsync(CancellationToken cancellationToken)
        {
            Calls.Add("wait");

            return Task.FromResult(true);
        }
    }
}
