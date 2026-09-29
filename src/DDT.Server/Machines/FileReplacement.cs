// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Server.Machines;

// A file that machines download is written next to the one it replaces and renamed over it, so a machine never
// downloads half of one.
internal static class FileReplacement
{
    // A new file name next to path. Creates the folder if it doesn't exist.
    public static string TemporaryFor(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        return $"{path}.{Guid.NewGuid():N}.upload";
    }
}
