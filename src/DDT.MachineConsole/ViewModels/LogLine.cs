// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.ConsoleProtocol;
using DDT.MachineConsole.Texts;

namespace DDT.MachineConsole.ViewModels;

public sealed class LogLine(Localizer localizer, ConsoleLogLine line) : ObservableObject
{
    public ConsoleLogLine Line => line;

    public string Time => Say.Time(line.Time);

    public string Level => Say.Level(localizer, line.Level);

    // As the agent wrote it, never translated.
    public string Text => line.Text;

    public bool IsWarning => line.Level == ConsoleLogLevel.Warning;

    public bool IsError => line.Level == ConsoleLogLevel.Error;

    public void Refresh() => Raise(nameof(Level));
}
