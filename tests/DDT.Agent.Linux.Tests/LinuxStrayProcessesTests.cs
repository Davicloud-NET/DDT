// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Agent.Deployment;
using Xunit;

namespace DDT.Agent.Tests;

// What a stopped script started and left outside its own tree, found in a made-up /proc. The agent is process 100.
public sealed class LinuxStrayProcessesTests : IDisposable
{
    private const int Agent = 100;

    private readonly LinuxTree _tree = new();
    private readonly StringWriter _console = new();
    private const int TerminateSignal = 15;

    private readonly List<int> _asked = [];
    private readonly List<int> _killed = [];
    private readonly HashSet<int> _immortal = [];
    private readonly HashSet<int> _obedient = [];

    public LinuxStrayProcessesTests()
    {
        Process(1, 0, 10, "init");
        Process(Agent, 1, 1000, "ddt-agent");
    }

    public void Dispose() => _tree.Dispose();

    // A line of /proc/<id>/stat as the kernel writes it, with the fields nobody reads here as zeroes.
    private void Process(int id, int parent, long started, string name = "sleep", char state = 'S') =>
        _tree.Write($"{id} ({name}) {state} {parent} {id} {id} 0 -1 4194304 {string.Join(' ', Enumerable.Repeat("0", 12))} {started} 0 0\n", "proc", $"{id}", "stat");

    // What the log got, without each line's time.
    private string[] Lines() =>
        [.. _console.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries).Select(line => line[9..])];

    private int Gone(int process)
    {
        Directory.Delete(_tree.PathOf("proc", $"{process}"), recursive: true);

        return 0;
    }

    // Ending a process takes its folder away, as the kernel does once the process is gone. Only a process the test
    // names goes when it is asked to.
    private LinuxStrayProcesses Strays(bool adopts = true) => new(
        _tree.Root,
        new AgentLog(new ImmediateTimeProvider(), _console),
        Agent,
        (process, signal) =>
        {
            if (signal == TerminateSignal)
            {
                _asked.Add(process);

                return _obedient.Contains(process) ? Gone(process) : 0;
            }

            _killed.Add(process);

            return _immortal.Contains(process) ? 1 : Gone(process);
        },
        _ => { })
    {
        Adopts = adopts,
    };

    // The script was process 200. Its own tree is gone already. One of its children had put itself into a session of
    // its own, another had been left by a child that ended: the kernel gave both to the agent.
    [Fact]
    public void EndsWhatTheScriptStartedThatTheAgentInherited()
    {
        Process(200, Agent, 5000, "sh", state: 'Z');
        Process(301, Agent, 5010, "sleep");
        Process(302, Agent, 5020, "my (odd) name");
        Process(303, 302, 5030);
        IToolStop stop = Strays().WatchFrom(200);

        stop.End();

        Assert.Equal([301, 302, 303], _killed.Order());
        Assert.Contains("Ended 3 more of its processes, which it had started outside its own tree.", _console.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("WARN", _console.ToString(), StringComparison.Ordinal);
    }

    // The script, a child in its tree and one the agent inherited all end on SIGTERM. Nothing is ended outright.
    [Fact]
    public void AsksAScriptAndAllItStartedToEndAndSaysThatTheyDid()
    {
        Process(150, Agent, 4000, "left-by-an-earlier-script");
        Process(200, Agent, 5000, "sh");
        Process(301, 200, 5010);
        Process(302, Agent, 5020);
        _obedient.UnionWith([200, 301, 302]);
        IToolStop stop = Strays().WatchFrom(200);

        stop.Ask("sh");
        stop.End();

        Assert.Equal([200, 301, 302], _asked.Order());
        Assert.Empty(_killed);
        Assert.Equal("INFO  sh and the 2 processes it had started ended when they were asked to (SIGTERM).", Assert.Single(Lines()));
    }

    [Fact]
    public void AScriptAloneThatEndsWhenAskedIsSaidInOneLine()
    {
        Process(200, Agent, 5000, "sh");
        _obedient.Add(200);

        Strays().WatchFrom(200).Ask("sh");

        Assert.Equal("INFO  sh ended when it was asked to (SIGTERM).", Assert.Single(Lines()));
    }

    // A script with a trap that never returns, and a child that ignores the signal. Both are there after the grace.
    [Fact]
    public void EndsOutrightWhatIsStillThereAfterTheGraceAndSaysWhichItTook()
    {
        Process(200, Agent, 5000, "sh");
        Process(301, 200, 5010);
        Process(302, Agent, 5020);
        _obedient.Add(302);
        IToolStop stop = Strays().WatchFrom(200);

        stop.Ask("sh");

        Assert.Empty(_killed);
        Assert.Equal(
            "WARN  2 processes of sh still ran 5 s after sh was asked to end (SIGTERM), so what is left is ended outright (SIGKILL).",
            Assert.Single(Lines()));

        stop.End();

        Assert.Equal([200, 301], _killed.Order());
    }

    // The tool ended by itself between its time limit and the stop, and left nothing.
    [Fact]
    public void AsksNobodyAndSaysNothingWhenTheToolAndItsProcessesAreGone()
    {
        Process(200, Agent, 5000, "sh");
        IToolStop stop = Strays().WatchFrom(200);
        Directory.Delete(_tree.PathOf("proc", "200"), recursive: true);

        stop.Ask("sh");

        Assert.Empty(_asked);
        Assert.Empty(Lines());
    }

    // What an earlier script left running on purpose, what other programs started and what has ended already.
    [Fact]
    public void LeavesAloneWhatIsOlderThanTheScriptOrNotBelowTheAgentOrGone()
    {
        Process(150, Agent, 4000, "left-by-an-earlier-script");
        Process(200, Agent, 5000, "sh");
        Process(400, 1, 6000, "udhcpc");
        Process(401, 400, 6001);
        Process(302, Agent, 5020, state: 'Z');
        IToolStop stop = Strays().WatchFrom(200);
        Directory.Delete(_tree.PathOf("proc", "200"), recursive: true);

        stop.End();

        Assert.Empty(_killed);
        Assert.Equal("", _console.ToString());
    }

    [Fact]
    public void SaysWhichProcessesItCouldNotEnd()
    {
        Process(200, Agent, 5000, "sh");
        Process(301, 200, 5010);
        _immortal.Add(301);
        IToolStop stop = Strays().WatchFrom(200);
        Directory.Delete(_tree.PathOf("proc", "200"), recursive: true);
        Process(301, Agent, 5010);

        stop.End();

        Assert.Equal(20, _killed.Count);
        Assert.Contains("1 of its processes could not be ended and may still run: 301.", _console.ToString(), StringComparison.Ordinal);
    }

    // Without the kernel's help an orphan goes to the first process, where nothing tells whose it was.
    [Fact]
    public void SaysWhatMaySurviveWhereTheKernelHandsTheAgentNoOrphans()
    {
        Process(200, Agent, 5000, "sh");
        Process(301, 1, 5010);

        Strays(adopts: false).WatchFrom(200).End();

        Assert.Equal([200], _killed);
        Assert.Contains(
            "This system does not hand the agent a process whose parent has ended, so one the tool left behind that way may still run.",
            _console.ToString(),
            StringComparison.Ordinal);
    }

    // A tool that had ended before anyone looked leaves nothing to tell its processes by, so nothing is ended.
    [Fact]
    public void EndsNothingWhenTheToolIsNotThereToBeWatched()
    {
        Process(301, Agent, 5010);

        Strays().WatchFrom(200).End();

        Assert.Empty(_killed);
    }
}
