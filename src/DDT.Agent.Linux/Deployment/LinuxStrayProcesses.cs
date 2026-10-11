// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Globalization;

namespace DDT.Agent.Deployment;

// Stops a script with what it started. The agent asks the kernel to become the parent of every process below it
// whose own parent ends, so such a process stays below the agent whatever session it moved to. A script shares the
// agent's process group, so each of its processes gets the signal itself. signal returns 0 or the error.
public sealed class LinuxStrayProcesses(string root, AgentLog log, int agent, Func<int, int, int>? signal = null, Action<TimeSpan>? wait = null) : IStrayProcesses
{
    // A process can start another while it is being ended, so the list is read again a few times.
    private const int Rounds = 20;

    private static readonly TimeSpan s_betweenRounds = TimeSpan.FromMilliseconds(50);

    private readonly Func<int, int, int> _signal = signal ?? Signal;
    private readonly Action<TimeSpan> _wait = wait ?? Thread.Sleep;

    // False where the kernel does not hand orphans to the agent. They go to the first process then, out of reach.
    public bool Adopts { get; init; }

    // How long a script has to end after it was asked to, for what its trap still has to do.
    public TimeSpan Grace { get; init; } = TimeSpan.FromSeconds(5);

    // Asks the kernel to hand this process every process below it whose parent ends, for as long as it lives. False
    // where the kernel refuses.
    public static bool Adopt() => StrayNativeMethods.prctl(StrayNativeMethods.SetChildSubreaper, 1, 0, 0, 0) == 0;

    public IToolStop WatchFrom(int processId) => new Stop(this, Read(processId)?.Started ?? long.MaxValue);

    // SIGTERM to every process below the agent that started no earlier than the tool, the tool among them. True when
    // none is left once the grace is over.
    public bool AskStartedSince(long since, string tool)
    {
        List<int> asked = Since(since);

        foreach (int process in asked)
        {
            _signal(process, StrayNativeMethods.TerminateSignal);
        }

        List<int> left = asked;

        for (TimeSpan waited = TimeSpan.Zero; left.Count > 0 && waited < Grace; waited += s_betweenRounds)
        {
            _wait(s_betweenRounds);
            left = Since(since);
        }

        if (left.Count > 0)
        {
            log.Warning(
                $"{Count(left.Count)} of {tool} still ran {Grace.TotalSeconds.ToString("0.#", CultureInfo.InvariantCulture)} s after " +
                $"{tool} was asked to end (SIGTERM), so what is left is ended outright (SIGKILL).");
        }
        else if (asked.Count > 1)
        {
            log.Information($"{tool} and the {Count(asked.Count - 1)} it had started ended when they were asked to (SIGTERM).");
        }
        else if (asked.Count == 1)
        {
            log.Information($"{tool} ended when it was asked to (SIGTERM).");
        }

        return left.Count == 0;
    }

    // SIGKILL to every process below the agent that started no earlier than the tool. What an earlier script left stays.
    public void EndStartedSince(long since)
    {
        HashSet<int> ended = [];
        List<int> left = [];

        for (int round = 0; round < Rounds; round++)
        {
            left = Since(since);

            if (left.Count == 0)
            {
                break;
            }

            foreach (int process in left.Where(process => _signal(process, StrayNativeMethods.KillSignal) == 0))
            {
                ended.Add(process);
            }

            _wait(s_betweenRounds);
        }

        Report(ended.Count, left);
    }

    private static string Count(int processes) => processes == 1 ? "1 process" : $"{processes} processes";

    private void Report(int ended, List<int> left)
    {
        if (ended > 0)
        {
            log.Information($"Ended {ended} more of its processes, which it had started outside its own tree.");
        }

        if (left.Count > 0)
        {
            log.Warning($"{left.Count} of its processes could not be ended and may still run: {string.Join(", ", left)}.");
        }

        if (!Adopts)
        {
            log.Warning("This system does not hand the agent a process whose parent has ended, so one the tool left behind that way may still run.");
        }
    }

    private List<int> Since(long since) => [.. Below().Where(process => process.Started >= since).Select(process => process.Id)];

    // The living processes that descend from the agent. One that has ended and waits to be collected is not among them.
    private List<(int Id, long Started)> Below()
    {
        Dictionary<int, (int Parent, long Started, bool Alive)> all = [];

        foreach (string folder in Folders())
        {
            if (int.TryParse(Path.GetFileName(folder), NumberStyles.None, CultureInfo.InvariantCulture, out int id) && Read(id) is { } process)
            {
                all[id] = process;
            }
        }

        return [.. all.Where(process => process.Value.Alive && IsBelow(process.Key, all)).Select(process => (process.Key, process.Value.Started))];
    }

    private bool IsBelow(int process, Dictionary<int, (int Parent, long Started, bool Alive)> all)
    {
        int current = process;

        // A chain longer than the list has a circle in it, which a list read while it changes can show.
        for (int steps = 0; steps <= all.Count && all.TryGetValue(current, out (int Parent, long Started, bool Alive) entry); steps++)
        {
            if (entry.Parent == agent)
            {
                return process != agent;
            }

            current = entry.Parent;
        }

        return false;
    }

    private string[] Folders()
    {
        try
        {
            return Directory.GetDirectories(Path.Combine(root, "proc"));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    // /proc/<id>/stat: the id, the name in brackets, which may hold spaces and brackets, then the state, the parent and,
    // as the nineteenth field after that, the start in clock ticks since the system started.
    private (int Parent, long Started, bool Alive)? Read(int process)
    {
        string text;

        try
        {
            text = File.ReadAllText(Path.Combine(root, "proc", process.ToString(CultureInfo.InvariantCulture), "stat"));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }

        string[] fields = text[(text.LastIndexOf(')') + 1)..].Split(' ', StringSplitOptions.RemoveEmptyEntries);

        if (fields.Length < 20
            || !int.TryParse(fields[1], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int parent)
            || !long.TryParse(fields[19], NumberStyles.None, CultureInfo.InvariantCulture, out long started))
        {
            return null;
        }

        return (parent, started, fields[0] is not ("Z" or "X"));
    }

    private static int Signal(int process, int signal) =>
        StrayNativeMethods.kill(process, signal) == 0 ? 0 : System.Runtime.InteropServices.Marshal.GetLastPInvokeError();

    // One tool's stop, by the moment the tool started.
    private sealed class Stop(LinuxStrayProcesses strays, long since) : IToolStop
    {
        public void Ask(string tool) => strays.AskStartedSince(since, tool);

        public void End() => strays.EndStartedSince(since);
    }
}
