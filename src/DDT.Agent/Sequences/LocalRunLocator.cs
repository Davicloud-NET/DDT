// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Agent.Sequences;

// Finds a run's state after Windows PE restarted, on whatever letter Windows PE gave the Windows volume this time:
// the newest DDT\run\state.json in roots, which are the fixed drives that are ready, other than Windows PE's own X:.
public sealed class LocalRunLocator(IEnumerable<string> roots)
{
    // Listed again at every Find, so a Windows volume that got its letter after the agent started is found too.
    public static IEnumerable<string> FixedDrives()
    {
        string? own = Path.GetPathRoot(Environment.SystemDirectory);

        foreach (DriveInfo drive in DriveInfo.GetDrives())
        {
            if (drive.DriveType == DriveType.Fixed
                && drive.IsReady
                && !string.Equals(drive.RootDirectory.FullName, own, StringComparison.OrdinalIgnoreCase))
            {
                yield return drive.RootDirectory.FullName;
            }
        }
    }

    // The root of the Windows volume holding the run, such as C:\, or null when none holds one.
    public string? Find()
    {
        string? newest = null;
        DateTime newestWrite = DateTime.MinValue;

        foreach (string root in roots)
        {
            try
            {
                FileInfo state = new(RunFiles.StatePathIn(root));

                if (state.Exists && state.LastWriteTimeUtc > newestWrite)
                {
                    newest = root;
                    newestWrite = state.LastWriteTimeUtc;
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // A drive that cannot be read holds no run this agent can go on with.
            }
        }

        return newest;
    }
}
