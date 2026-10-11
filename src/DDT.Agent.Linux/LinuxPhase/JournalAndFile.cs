// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Text;

namespace DDT.Agent.LinuxPhase;

// Writes the agent's lines to the unit's output, which systemd puts into the journal, and to the agent's own file. The
// journal always gets a line. A file that cannot take one, as on a full disk, must not end the agent.
internal sealed class JournalAndFile(TextWriter journal, TextWriter? file) : TextWriter
{
    public override Encoding Encoding => journal.Encoding;

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
