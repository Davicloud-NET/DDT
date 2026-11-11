// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Runtime.InteropServices;
using System.Text;

namespace DDT.Agent.Deployment;

// Windows' ANSI code page, which older programs such as tree write into a pipe, and its OEM one, which dism writes.
public static partial class AnsiCodePage
{
    private const uint Ansi = 0;
    private const uint Oem = 1;

    public static string Decode(ReadOnlySpan<byte> bytes) => Decode(Ansi, bytes);

    public static string DecodeOem(ReadOnlySpan<byte> bytes) => Decode(Oem, bytes);

    public static unsafe string Decode(uint codePage, ReadOnlySpan<byte> bytes)
    {
        fixed (byte* input = bytes)
        {
            int length = MultiByteToWideChar(codePage, 0, input, bytes.Length, null, 0);

            if (length <= 0)
            {
                return Encoding.Latin1.GetString(bytes);
            }

            char[] text = new char[length];

            fixed (char* output = text)
            {
                return new string(output, 0, MultiByteToWideChar(codePage, 0, input, bytes.Length, output, length));
            }
        }
    }

    // Win32's signature, however many parameters it takes.
    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static unsafe partial int MultiByteToWideChar(uint codePage, uint flags, byte* input, int inputLength, char* output, int outputLength);
}
