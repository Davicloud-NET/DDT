// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Agent.Deployment;
using DDT.Agent.LinuxPhase;
using DDT.Agent.Sequences;
using DDT.Contracts.Agents;
using DDT.Contracts.Deployments;
using DDT.Contracts.Sequences;
using DDT.Core.CloudInit;
using Xunit;

namespace DDT.Agent.Tests;

// The path ddt-agent.service takes, from its start to its exit code, over a folder that stands in for /. The server,
// the kernel's mounts and systemd are stood in for. The agent's own parts are the unit's: its tool runner, its
// launcher, its seeds, its log.
public sealed class LinuxPhaseUnitTests : IDisposable
{
    private const string StartId = "8f2c6f6e-0f5c-4d43-9d1d-4a1f0e0c2a01";

    private const string Crash = "ERROR The agent stopped on an unexpected error: System.InvalidOperationException: made up for the test";

    private readonly LinuxPhaseFixture _linux = new();
    private readonly StringWriter _journal = new();

    // What systemd stops the unit with.
    private readonly CancellationTokenSource _unit = new();
    private AnsweringSystemd? _systemd;

    public void Dispose()
    {
        _linux.Dispose();
        _journal.Dispose();
        _unit.Dispose();
    }

    private LinuxAgentPaths Paths => _linux.Paths;

    private List<string> Systemd => _systemd?.Calls ?? [];

    private LinuxPhaseMachine Machine(IAgentServer? server, string? processPath = null, Exception? systemdFails = null) => new(Paths, _journal, processPath ?? Paths.Agent)
    {
        Connect = _ => server ?? throw new InvalidOperationException("made up for the test"),
        Mounts = _linux.Mounts,
        Tools = real => _systemd = new AnsweringSystemd(real, _unit) { Failure = systemdFails },
        Identity = new DryRunMachineIdentityReader(1),
    };

    private Task<int> RunAsync(IAgentServer? server) => LinuxPhaseService.RunAsync(Machine(server), _unit.Token);

    private static ScriptedAgentServer Server(AgentRun run) =>
        new ScriptedAgentServer()
            .OnRegister(_ => LinuxPhaseFixture.Continued())
            .OnNext(_ => LinuxPhaseFixture.Next("session-1", run));

    // The journal's lines without their date and time, each with its level.
    private string[] Lines() =>
        [.. _journal.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries).Select(line => line.Length > 24 ? line[24..] : line)];

    // A seed the kernel lists by its label. Mounting it shows an empty volume unless the test puts something there.
    private void SeedDevice() => _linux.Put(Paths.At($"/dev/disk/by-label/{CloudInitSeed.Label}"));

    // A finished run ends with exit code 4 inside the agent. systemd must see 0, or it keeps a failed unit that has
    // no file any more.
    [Fact]
    public async Task ARunThatIsDoneEndsTheUnitWithZeroAndLeavesNothingOfTheAgent()
    {
        AgentRun run = LinuxPhaseFixture.Run();
        await _linux.HandOverAsync(run);
        SeedDevice();
        ScriptedAgentServer server = Server(run);

        int exitCode = await RunAsync(server);

        Assert.Equal(0, exitCode);
        Assert.Equal(DeploymentState.Done, server.RunReports[^1].State);
        Assert.Equal(AgentEnvironment.Linux, Assert.Single(server.Registrations).Environment);
        Assert.Equal(["systemctl disable ddt-agent.service", "systemctl daemon-reload"], Systemd);
        Assert.False(Directory.Exists(Paths.State));
        Assert.False(Directory.Exists(Paths.Program));
        Assert.False(File.Exists(Paths.Unit));
        Assert.Contains(Lines(), line => line.StartsWith("INFO  DDT agent ", StringComparison.Ordinal) && line.EndsWith(", the ddt-agent.service unit of the installed Linux.", StringComparison.Ordinal));
        Assert.Equal($"INFO  {LinuxAgentRemoval.Removed}", Lines()[^1]);
        Assert.DoesNotContain(Lines(), line => !line.StartsWith("INFO", StringComparison.Ordinal));
    }

    // A restart the run asks for ends the agent with 5. That, too, is 0 for systemd, which starts the unit again
    // with the machine.
    [Fact]
    public async Task ARestartTheRunAsksForEndsTheUnitWithZeroAndStaysDueUntilLinuxRestarts()
    {
        AgentRun run = LinuxPhaseFixture.Run(new RebootStep { Id = Guid.Parse("0193a4b2-0000-7000-8000-00000000b004"), Name = "Restart" });
        await _linux.HandOverAsync(run);

        int exitCode = await RunAsync(Server(run));

        Assert.Equal(0, exitCode);
        Assert.Equal(["systemctl reboot"], Systemd);
        Assert.True(File.Exists(Path.Combine(Paths.Run, LinuxRestartMarker.FileName)));
        Assert.True(File.Exists(Paths.Unit));
        Assert.Contains("INFO  Linux restarts, and the run goes on after the restart.", Lines());

        // The same lines are in the agent's own file, which only root reads.
        Assert.Equal(Lines().Length, File.ReadAllLines(Paths.LogFile).Length);
    }

    // Nobody expected it, so the journal gets it whole, and exit code 1 lets systemd start the unit again.
    [Fact]
    public async Task AnUnexpectedErrorEndsTheUnitWithOneAndTheJournalSaysWhatItWas()
    {
        await _linux.HandOverAsync(LinuxPhaseFixture.Run());

        int exitCode = await RunAsync(null);

        Assert.Equal(LinuxPhaseService.Crashed, exitCode);
        Assert.Contains(Lines(), line => line.StartsWith(Crash, StringComparison.Ordinal));
        Assert.Contains("made up for the test", File.ReadAllText(Paths.LogFile), StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnUnexpectedErrorWithoutItsSettingsEndsTheUnitWithOneToo()
    {
        await _linux.HandOverAsync(LinuxPhaseFixture.Run());
        File.Delete(Paths.Configuration);
        LinuxPhaseMachine machine = Machine(null, systemdFails: new InvalidOperationException("made up for the test"));

        int exitCode = await LinuxPhaseService.RunAsync(machine, _unit.Token);

        Assert.Equal(LinuxPhaseService.Crashed, exitCode);
        Assert.Contains(Lines(), line => line.StartsWith(Crash, StringComparison.Ordinal));
    }

    // The power went half way through a removal: the settings are gone, the unit and the program are not. The unit
    // starts the program once more, and it finishes what is left.
    [Fact]
    public async Task ARemovalThatWasCutOffIsFinishedWhenTheUnitStartsTheAgentWithoutItsSettings()
    {
        await _linux.HandOverAsync(LinuxPhaseFixture.Run(LinuxPhaseFixture.Script(1)));
        File.Delete(Paths.Configuration);
        _linux.Put(Path.Combine(_linux.RunDirectory, "scripts", $"{LinuxPhaseFixture.Script(1).Id:D}.sh"), "echo 1\n");
        SeedDevice();

        int exitCode = await RunAsync(null);

        Assert.Equal(0, exitCode);
        Assert.Equal($"WARN  {LinuxPhaseService.NoSettings(Paths.Configuration)}", Lines()[0]);
        Assert.Equal(["systemctl disable ddt-agent.service", "systemctl daemon-reload"], Systemd);
        Assert.False(Directory.Exists(Paths.State));
        Assert.False(Directory.Exists(Paths.Program));
        Assert.False(File.Exists(Paths.Unit));
        Assert.Equal($"INFO  {LinuxAgentRemoval.Removed}", Lines()[^1]);
    }

    // On some systems /usr/local is a link, and the kernel names a running program by where it really is.
    [Fact]
    [Trait("Category", "LinuxKernel")]
    public async Task KnowsItsOwnProgramAlsoWhereUsrLocalIsALink()
    {
        LoopDisk.SkipUnlessLinux();
        await _linux.HandOverAsync(LinuxPhaseFixture.Run());
        File.Delete(Paths.Configuration);
        SeedDevice();
        string local = Paths.At("/usr/local");
        string real = Paths.At("/opt/local");
        Directory.CreateDirectory(Path.GetDirectoryName(real)!);
        Directory.Move(local, real);
        Directory.CreateSymbolicLink(local, real);

        int exitCode = await LinuxPhaseService.RunAsync(Machine(null, processPath: Path.Combine(real, "lib", "ddt", "ddt-agent")), _unit.Token);

        Assert.Equal(0, exitCode);
        Assert.False(File.Exists(Paths.Unit));
        Assert.False(File.Exists(Path.Combine(real, "lib", "ddt", "ddt-agent")));
    }

    // Each of the three comes before the first call to the server: a restart may stop the unit within a second, a
    // token on the seed is one too many, and what a killed agent left mounted is nobody's any more.
    [Fact]
    public async Task BeforeItReachesTheServerItNotesItsStartTakesATokenOffTheSeedAndDisconnectsOldShares()
    {
        AgentRun run = LinuxPhaseFixture.Run();
        await _linux.HandOverAsync(run);
        _linux.Put(Paths.At(CloudInitFiles.StartIdPath), StartId + "\n");
        string oldShare = Path.Combine(Paths.Shares, "0193a4b2-0000-7000-8000-00000000c001", "1");
        Directory.CreateDirectory(oldShare);
        _linux.Mounts.Mount("//files.example.test/drivers", oldShare, LinuxShareConnector.FileSystem, string.Empty);
        SeedDevice();
        string token = Path.Combine(Paths.SeedMount, LinuxAgentFiles.SeedFolder, "token");
        _linux.Mounts.Mounted = (_, target) => _linux.Put(Path.Combine(target, LinuxAgentFiles.SeedFolder, "token"), "run-token-1");
        ScriptedAgentServer server = Server(run);
        List<string> atTheFirstCall = [];
        LinuxPhaseMachine machine = Machine(server) with
        {
            Connect = _ =>
            {
                atTheFirstCall.Add(File.Exists(Paths.FirstStart) ? File.ReadAllText(Paths.FirstStart) : "no note");
                atTheFirstCall.Add(File.Exists(token) ? "token on the seed" : "no token on the seed");
                atTheFirstCall.Add(_linux.Mounts.Calls.Contains($"unmount {oldShare} detached") ? "old share gone" : "old share mounted");
                _linux.Mounts.Mounted = null;

                return server;
            },
        };

        int exitCode = await LinuxPhaseService.RunAsync(machine, _unit.Token);

        Assert.Equal(0, exitCode);
        Assert.Equal([StartId, "no token on the seed", "old share gone"], atTheFirstCall);
        Assert.Equal($"WARN  {SeedTokenRepair.Repaired}", Lines()[0]);
        Assert.Equal($"WARN  Disconnected a share an earlier start of the agent left at {oldShare}.", Lines()[1]);
        Assert.DoesNotContain("run-token", _journal.ToString(), StringComparison.Ordinal);
    }

    // A real script through the unit's own tool runner and launcher: where it runs, what it finds in its environment
    // and with which mask, where its share is, and what its restart code leads to.
    [Fact]
    [Trait("Category", "LinuxKernel")]
    public async Task RunsARealScriptAsTheUnitDoesAndRestartsLinuxForItsRestartCode()
    {
        LoopDisk.SkipUnlessLinux();
        RunScriptStep script = LinuxPhaseFixture.Script(1) with
        {
            Script = "pwd\necho \"phase=$DDT_PHASE home=$HOME\"\necho \"path=$PATH\"\necho \"share=$DDT_SHARE_1\"\necho \"mask=$(umask)\"\nexit 194\n",
            Shares = [new ShareConnection(@"\\files.example.test\drivers", new AccountReference(Guid.NewGuid(), null))],
        };
        AgentRun run = LinuxPhaseFixture.Run(script, LinuxPhaseFixture.Script(2));
        await _linux.HandOverAsync(run);
        ScriptedAgentServer server = Server(run)
            .OnRunStepAccounts(_ => new AgentStepAccounts(null, [new AgentShareConnection(@"\\10.0.2.2\drivers", "ddt", "made-up")]));
        string scripts = Path.Combine(_linux.RunDirectory, "scripts");
        string share = Path.Combine(Paths.Shares, $"{script.Id:D}", "1");

        int exitCode = await RunAsync(server);

        Assert.Equal(0, exitCode);
        string[] lines = Lines();
        Assert.Contains($"INFO  Running /bin/sh {Path.Combine(scripts, $"{script.Id:D}.sh")}", lines);
        Assert.Contains($"INFO  {scripts}", lines);
        Assert.Contains($"INFO  phase=Linux home={Environment.GetEnvironmentVariable("HOME") ?? LinuxShell.DefaultHome}", lines);
        Assert.Contains($"INFO  path={Environment.GetEnvironmentVariable("PATH") ?? LinuxShell.DefaultPath}", lines);
        Assert.Contains($"INFO  share={share}", lines);
        Assert.Contains("INFO  mask=0022", lines);
        Assert.Contains("INFO  The script ended with exit code 194, which asks for a restart before the next step.", lines);
        Assert.Equal(["systemctl reboot"], Systemd);
        Assert.Equal([$"mount cifs //10.0.2.2/drivers at {share}", $"unmount {share} detached"], _linux.Mounts.Calls);
        Assert.DoesNotContain(lines, line => line.Contains("Script 2", StringComparison.Ordinal));
        Assert.DoesNotContain("made-up", _journal.ToString(), StringComparison.Ordinal);
    }
}
