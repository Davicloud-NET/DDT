// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Globalization;

namespace DDT.Agent.Deployment;

// Finds what a script started and no longer holds. The agent asks the kernel to become the parent of every process
// below it whose own parent ends, so such a process stays below the agent whatever session it moved to. kill returns
// 0 or the error.
public sealed class LinuxStrayProcesses(string root, AgentLog log, int agent, Func<int, int>? kill = null, Action<TimeSpan>? wait = null) : IStrayProcesses
{
    // A process can start another while it is being ended, so the list is read again a few times.
    private const int Rounds = 20;

    private static readonly TimeSpan s_betweenRounds = TimeSpan.FromMilliseconds(50);

    private readonly Func<int, int> _kill = kill ?? Kill;
    private readonly Action<TimeSpan> _wait = wait ?? Thread.Sleep;

    // False where the kernel does not hand orphans to the agent. They go to the first process then, out of reach.
    public bool Adopts { get; init; }

    // Asks the kernel to hand this process every process below it whose parent ends, for as long as it lives. False
    // where the kernel refuses.
    public static bool Adopt() => StrayNativeMethods.prctl(StrayNativeMethods.SetChildSubreaper, 1, 0, 0, 0) == 0;

    public Action WatchFrom(int processId)
    {
        long since = Read(processId)?.Started ?? long.MaxValue;

        return () => EndStartedSince(since);
    }

    // Ends every process below the agent that started no earlier than the tool. What an earlier script left stays.
    public void EndStartedSince(long since)
    {
        HashSet<int> ended = [];
        List<int> left = [];

        for (int round = 0; round < Rounds; round++)
        {
            left = [.. Below().Where(process => process.Started >= since).Select(process => process.Id)];

            if (left.Count == 0)
            {
                break;
            }

            foreach (int process in left.Where(process => _kill(process) == 0))
            {
                ended.Add(process);
            }

            _wait(s_betweenRounds);
        }

        Report(ended.Count, left);
    }

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

    private static int Kill(int process) =>
        StrayNativeMethods.kill(process, StrayNativeMethods.KillSignal) == 0 ? 0 : System.Runtime.InteropServices.Marshal.GetLastPInvokeError();
}
