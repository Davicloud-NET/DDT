// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Text;

namespace DDT.Server.Certificates;

internal static class PemFiles
{
    // Written beside the target and renamed into place, so a reader never sees half a file. A key is owner-only from the
    // moment it exists rather than after a change of mode.
    public static void Write(string path, string pem, bool isKey)
    {
        string temporary = path + ".tmp";
        File.Delete(temporary);

        FileStreamOptions options = new() { Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None };

        if (isKey && !OperatingSystem.IsWindows())
        {
            options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        }

        using (StreamWriter writer = new(temporary, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), options))
        {
            writer.Write(pem);
        }

        File.Move(temporary, path, overwrite: true);
    }
}
