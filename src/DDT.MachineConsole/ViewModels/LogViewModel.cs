// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Collections.ObjectModel;
using DDT.ConsoleProtocol;
using DDT.MachineConsole.Texts;

namespace DDT.MachineConsole.ViewModels;

// The agent's log as it arrives: level, time and text. It keeps the newest lines, follows the end while it is at the
// end, and stays where it is once scrolled up, until End or the key to follow brings it back.
public sealed class LogViewModel(Localizer localizer) : OverlayViewModel(localizer)
{
    // As many lines as the agent keeps for a console that reads slowly.
    public const int MaxLines = 5000;

    private bool _follows = true;

    public ObservableCollection<LogLine> Lines { get; } = [];

    public bool Follows
    {
        get => _follows;
        set => Set(ref _follows, value);
    }

    public string Title => T("Log");

    public string Hint => T("The agent's log, newest line last. Times are in UTC, by this machine's clock.");

    public string Empty => T("The agent has written nothing yet.");

    public bool IsEmpty => Lines.Count == 0;

    public string FollowLabel => T("Follow the end");

    public override string CloseLabel => T("Close the log");

    public void Append(IReadOnlyList<ConsoleLogLine> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);

        bool wasEmpty = Lines.Count == 0;

        foreach (ConsoleLogLine line in lines)
        {
            Lines.Add(new LogLine(L, line));
        }

        while (Lines.Count > MaxLines)
        {
            Lines.RemoveAt(0);
        }

        if (wasEmpty && Lines.Count > 0)
        {
            Raise(nameof(IsEmpty));
        }
    }

    // For an agent that sends its newest lines again as it connects anew.
    public void Clear()
    {
        if (Lines.Count == 0)
        {
            return;
        }

        Lines.Clear();
        Raise(nameof(IsEmpty));
    }

    public override void Refresh()
    {
        foreach (LogLine line in Lines)
        {
            line.Refresh();
        }

        base.Refresh();
    }
}

public sealed class LogLine(Localizer localizer, ConsoleLogLine line) : ObservableObject
{
    public ConsoleLogLine Line => line;

    public string Time => Say.Time(line.Time);

    public string Level => Say.Level(localizer, line.Level);

    // The agent's words.
    public string Text => line.Text;

    public bool IsWarning => line.Level == ConsoleLogLevel.Warning;

    public bool IsError => line.Level == ConsoleLogLevel.Error;

    public void Refresh() => Raise(nameof(Level));
}
