// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Text;
using DDT.Contracts.Agents;

namespace DDT.Agent.LinuxPhase;

// Writes the agent's lines to the unit's output, which systemd puts into the journal, and to the agent's own file. The
// journal always gets a line. A file that cannot take one, as on a full disk, must not end the agent. With
// prioritized, a warning and an error carry the prefix systemd reads as their priority and takes off again.
internal sealed class JournalAndFile(TextWriter journal, TextWriter? file, bool prioritized = false) : TextWriter, ILeveledOutput
{
    // SD_WARNING and SD_ERR of sd-daemon.h.
    public const string WarningPrefix = "<4>";
    public const string ErrorPrefix = "<3>";

    public override Encoding Encoding => journal.Encoding;

    // systemd reads the prefix at the start of each line, so every line of a text that has several gets it.
    public void WriteLine(AgentLogLevel level, string line)
    {
        string? prefix = !prioritized ? null : level switch
        {
            AgentLogLevel.Warning => WarningPrefix,
            AgentLogLevel.Error => ErrorPrefix,
            _ => null,
        };

        journal.WriteLine(prefix is null ? line : prefix + line.ReplaceLineEndings(Environment.NewLine + prefix));
        ToFile(writer => writer.WriteLine(line));
    }

    public override void Write(char value)
    {
        journal.Write(value);
        ToFile(writer => writer.Write(value));
    }

    public override void Write(string? value)
    {
        journal.Write(value);
        ToFile(writer => writer.Write(value));
    }

    public override void WriteLine(string? value)
    {
        journal.WriteLine(value);
        ToFile(writer => writer.WriteLine(value));
    }

    public override void Flush()
    {
        journal.Flush();
        ToFile(writer => writer.Flush());
    }

    // The journal is the process's own output and stays open.
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            ToFile(writer => writer.Dispose());
        }

        base.Dispose(disposing);
    }

    private void ToFile(Action<TextWriter> write)
    {
        try
        {
            if (file is not null)
            {
                write(file);
            }
        }
        catch (Exception exception) when (exception is IOException or ObjectDisposedException)
        {
            // The journal has the line
        }
    }
}
