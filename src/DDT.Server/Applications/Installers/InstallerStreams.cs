// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Server.Applications.Installers;

// Reads at an offset of an uploaded installer. A read past its end means the file is damaged, not that the disk failed.
internal static class InstallerStreams
{
    public static void ReadExactlyAt(this Stream installer, long offset, Span<byte> buffer)
    {
        if (offset < 0 || offset > installer.Length - buffer.Length)
        {
            throw new InvalidDataException("A read reaches past the end of the file.");
        }

        installer.Position = offset;
        installer.ReadExactly(buffer);
    }

    // Up to count bytes from offset, fewer at the end of the file.
    public static byte[] ReadAtMost(this Stream installer, long offset, int count)
    {
        if (offset < 0 || offset >= installer.Length || count <= 0)
        {
            return [];
        }

        byte[] buffer = new byte[(int)Math.Min(count, installer.Length - offset)];
        installer.ReadExactlyAt(offset, buffer);

        return buffer;
    }
}
