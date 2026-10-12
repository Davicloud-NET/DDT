// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Globalization;
using System.Runtime.InteropServices;

namespace DDT.Agent.Deployment;

// Stops a script with what it started. The agent asks the kernel to become the parent of every process below it
// whose own parent ends, so such a process stays below the agent whatever session it moved to. A script shares the
// agent's process group, so each of its processes gets the signal itself. signal returns 0 or the error.
public sealed class LinuxStrayProcesses(string root, AgentLog log, int agent, Func<int, int, int>? signal = null, Func<TimeSpan, Task>? wait = null) : IStrayProcesses
{
    // A process can start another while it is being ended, so the list is read again a few times.
    private const int Rounds = 20;

    private static readonly TimeSpan s_betweenRounds = TimeSpan.FromMilliseconds(50);

    private readonly Func<TimeSpan, Task> _wait = wait ?? (time => Task.Delay(time, TimeProvider.System));

    // False where the kernel does not hand orphans to the agent. They go to the first process then, out of reach.
    public bool Adopts { get; init; }

    // True while systemd stops the agent's unit. It has sent SIGTERM to every process of the unit by then, the script's
    // among them, and a second one would run a script's trap twice.
    public Func<bool>? UnitIsStopping { get; init; }

    // How long a script has to end after it was asked to, for what its trap still has to do.
    public TimeSpan Grace { get; init; } = TimeSpan.FromSeconds(5);

    // Asks the kernel to hand this process every process below it whose parent ends, for as long as it lives. False
    // where the kernel refuses.
    public static bool Adopt() => StrayNativeMethods.prctl(StrayNativeMethods.SetChildSubreaper, 1, 0, 0, 0) == 0;

    public IToolStop WatchFrom(int processId) => new Stop(this, Read(processId)?.Started ?? long.MaxValue);

    // SIGTERM to every process below the agent that started no earlier than the tool, the tool among them. True when
    // none is left once the grace is over.
    public async Task<bool> AskStartedSinceAsync(long since, string tool)
    {
        bool bySystemd = UnitIsStopping?.Invoke() == true;
        List<Listed> asked = Since(since);

        foreach (Listed process in bySystemd ? [] : asked)
        {
            Send(process, StrayNativeMethods.TerminateSignal);
        }

        List<Listed> left = asked;

        for (TimeSpan waited = TimeSpan.Zero; left.Count > 0 && waited < Grace; waited += s_betweenRounds)
        {
            await _wait(s_betweenRounds).ConfigureAwait(false);
            left = Since(since);
        }

        Say(tool, asked.Count, left.Count, bySystemd);

        return left.Count == 0;
    }

    // SIGKILL to every process below the agent that started no earlier than the tool. What an earlier script left stays.
    public async Task EndStartedSinceAsync(long since)
    {
        HashSet<int> ended = [];
        List<Listed> left = [];

        for (int round = 0; round < Rounds; round++)
        {
            left = Since(since);

            if (left.Count == 0)
            {
                break;
            }

            foreach (Listed process in left.Where(process => Send(process, StrayNativeMethods.KillSignal) == 0))
            {
                ended.Add(process.Id);
            }

            await _wait(s_betweenRounds).ConfigureAwait(false);
        }

        Report(ended.Count, [.. left.Select(process => process.Id)]);
    }

    // Who asked is said as it was: the agent, or systemd before it.
    private void Say(string tool, int asked, int left, bool bySystemd)
    {
        string grace = Grace.TotalSeconds.ToString("0.#", CultureInfo.InvariantCulture);
        string systemd = "systemd stopped the unit and asked every process of it to end (SIGTERM)";

        if (left > 0)
        {
            log.Warning(
                $"{Count(left)} of {tool} still ran {grace} s after {(bySystemd ? systemd : $"{tool} was asked to end (SIGTERM)")}, " +
                "so what is left is ended outright (SIGKILL).");
        }
        else if (asked > 1)
        {
            log.Information(bySystemd
                ? $"{tool} and the {Count(asked - 1)} it had started ended when {systemd}."
                : $"{tool} and the {Count(asked - 1)} it had started ended when they were asked to (SIGTERM).");
        }
        else if (asked == 1)
        {
            log.Information(bySystemd ? $"{tool} ended when {systemd}." : $"{tool} ended when it was asked to (SIGTERM).");
        }
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

    // The living processes below the agent that the tool started, or that it may have: none of them and none between
    // them and the agent is older than the tool. What a daemon of an earlier step starts later is that daemon's.
    // One that has ended and waits to be collected is not among them.
    private List<Listed> Since(long since)
    {
        Dictionary<int, (int Parent, long Started, bool Alive)> all = [];

        foreach (string folder in Folders())
        {
            if (int.TryParse(Path.GetFileName(folder), NumberStyles.None, CultureInfo.InvariantCulture, out int id) && Read(id) is { } process)
            {
                all[id] = process;
            }
        }

        return [.. all.Where(process => process.Value.Alive && IsBelow(process.Key, since, all)).Select(process => new Listed(process.Key, process.Value.Started))];
    }

    private bool IsBelow(int process, long since, Dictionary<int, (int Parent, long Started, bool Alive)> all)
    {
        int current = process;

        // A chain longer than the list has a circle in it, which a list read while it changes can show.
        for (int steps = 0; steps <= all.Count && all.TryGetValue(current, out (int Parent, long Started, bool Alive) entry); steps++)
        {
            if (entry.Started < since)
            {
                return false;
            }

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

    private int Send(Listed process, int number) => signal is null ? Signal(process, number) : signal(process.Id, number);

    // Through a descriptor of the process, where the kernel has them, since 5.3. The number alone may be another
    // process's by now, and a descriptor opened for that one shows another start than the one that was listed.
    private int Signal(Listed process, int number)
    {
        int descriptor = (int)StrayNativeMethods.syscall(StrayNativeMethods.PidfdOpen, process.Id, 0, 0, 0);

        if (descriptor < 0)
        {
            int error = Marshal.GetLastPInvokeError();

            return error == StrayNativeMethods.NoSuchCall ? Kill(process.Id, number) : error;
        }

        try
        {
            if (Read(process.Id)?.Started != process.Started)
            {
                return StrayNativeMethods.NoSuchProcess;
            }

            return StrayNativeMethods.syscall(StrayNativeMethods.PidfdSendSignal, descriptor, number, 0, 0) == 0 ? 0 : Marshal.GetLastPInvokeError();
        }
        finally
        {
            _ = StrayNativeMethods.close(descriptor);
        }
    }

    private static int Kill(int process, int number) =>
        StrayNativeMethods.kill(process, number) == 0 ? 0 : Marshal.GetLastPInvokeError();

    // A process as the list showed it: its number, and its start in clock ticks, which tells it from a later one.
    private readonly record struct Listed(int Id, long Started);

    // One tool's stop, by the moment the tool started.
    private sealed class Stop(LinuxStrayProcesses strays, long since) : IToolStop
    {
        public Task AskAsync(string tool) => strays.AskStartedSinceAsync(since, tool);

        public Task EndAsync() => strays.EndStartedSinceAsync(since);
    }
}
