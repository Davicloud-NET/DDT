// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Text;
using System.Text.Unicode;

namespace DDT.Agent.Deployment;

// Reads a tool's output line by line. A script writes UTF-8 after chcp 65001, but older programs such as tree write
// the ANSI code page into a pipe, and a script may run both. So a line that's valid UTF-8 is read as UTF-8, and any
// other line by the system's decoder. A carriage return alone ends a line too, because progress bars use it.
public static class ToolOutput
{
    public static async Task ForwardAsync(Stream stream, ToolOutputDecoder decoder, Action<string> write)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(decoder);
        ArgumentNullException.ThrowIfNull(write);

        byte[] buffer = new byte[4096];
        MemoryStream line = new();
        int read;

        while ((read = await stream.ReadAsync(buffer).ConfigureAwait(false)) > 0)
        {
            foreach (byte octet in buffer.AsSpan(0, read))
            {
                if (octet is (byte)'\n' or (byte)'\r')
                {
                    Emit(line, decoder, write);
                }
                else
                {
                    line.WriteByte(octet);
                }
            }
        }

        Emit(line, decoder, write);
    }

    public static string Decode(ReadOnlySpan<byte> bytes, ToolOutputDecoder decoder)
    {
        ArgumentNullException.ThrowIfNull(decoder);

        return Utf8.IsValid(bytes) ? Encoding.UTF8.GetString(bytes) : decoder(bytes);
    }

    private static void Emit(MemoryStream line, ToolOutputDecoder decoder, Action<string> write)
    {
        if (line.Length == 0)
        {
            return;
        }

        string text = Decode(line.GetBuffer().AsSpan(0, (int)line.Length), decoder);
        line.SetLength(0);

        if (!string.IsNullOrWhiteSpace(text))
        {
            write(text.TrimEnd());
        }
    }
}
