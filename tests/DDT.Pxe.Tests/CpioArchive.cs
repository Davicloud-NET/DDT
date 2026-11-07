// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Globalization;
using System.Text;

namespace DDT.Pxe.Tests;

// Reads a newc archive back the way the kernel does: header, name, content, each padded to four bytes.
internal static class CpioArchive
{
    public static List<(string Path, uint Mode, byte[] Content)> Entries(byte[] archive)
    {
        List<(string, uint, byte[])> entries = [];
        int offset = 0;

        while (true)
        {
            string header = Encoding.ASCII.GetString(archive, offset, 110);

            if (!header.StartsWith("070701", StringComparison.Ordinal))
            {
                throw new InvalidDataException("An entry does not start with the newc magic.");
            }

            uint Field(int index) => uint.Parse(header.AsSpan(6 + (index * 8), 8), NumberStyles.HexNumber, CultureInfo.InvariantCulture);

            int nameLength = (int)Field(11);
            int size = (int)Field(6);
            string name = Encoding.UTF8.GetString(archive, offset + 110, nameLength - 1);
            int content = Padded(offset + 110 + nameLength);

            if (name == "TRAILER!!!")
            {
                return entries;
            }

            entries.Add((name, Field(1), archive[content..(content + size)]));
            offset = Padded(content + size);
        }
    }

    private static int Padded(int offset) => (offset + 3) / 4 * 4;
}
