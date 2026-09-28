// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Runtime.InteropServices;
using System.Text;
using System.Text.Unicode;

namespace DDT.Agent.Deployment;

// A tool's output, line by line. A script writes UTF-8 after chcp 65001, but older programs such as tree write the ANSI
// code page into a pipe, and a script may run both: a line that is valid UTF-8 is read so, any other as ANSI. A carriage
// return alone ends a line too, as progress bars use it.
public static partial class ToolOutput
{
    private const uint AnsiCodePage = 0;

    public static async Task ForwardAsync(Stream stream, Action<string> write)
    {
        ArgumentNullException.ThrowIfNull(stream);
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
                    Emit(line, write);
                }
                else
                {
                    line.WriteByte(octet);
                }
            }
        }

        Emit(line, write);
    }

    public static string Decode(ReadOnlySpan<byte> bytes) =>
        Utf8.IsValid(bytes) ? Encoding.UTF8.GetString(bytes) : Ansi(bytes);

    private static void Emit(MemoryStream line, Action<string> write)
    {
        if (line.Length == 0)
        {
            return;
        }

        string text = Decode(line.GetBuffer().AsSpan(0, (int)line.Length));
        line.SetLength(0);

        if (!string.IsNullOrWhiteSpace(text))
        {
            write(text.TrimEnd());
        }
    }

    private static unsafe string Ansi(ReadOnlySpan<byte> bytes)
    {
        fixed (byte* input = bytes)
        {
            int length = MultiByteToWideChar(AnsiCodePage, 0, input, bytes.Length, null, 0);

            if (length <= 0)
            {
                return Encoding.Latin1.GetString(bytes);
            }

            char[] text = new char[length];

            fixed (char* output = text)
            {
                return new string(output, 0, MultiByteToWideChar(AnsiCodePage, 0, input, bytes.Length, output, length));
            }
        }
    }

    // Win32's signature, however many parameters it takes.
    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static unsafe partial int MultiByteToWideChar(uint codePage, uint flags, byte* input, int inputLength, char* output, int outputLength);
}
