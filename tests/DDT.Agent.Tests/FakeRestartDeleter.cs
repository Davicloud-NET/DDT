// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Agent.WindowsPhase;

namespace DDT.Agent.Tests;

// Stands in for Windows deleting at its next start. Every mark goes into the tools' journal, relative to the Windows
// volume.
internal sealed class FakeRestartDeleter(FakeDeploymentTools tools) : IRestartDeleter
{
    private readonly Lock _lock = new();
    private readonly List<string> _marked = [];

    public void DeleteAtRestart(string path)
    {
        lock (_lock)
        {
            _marked.Add(path);
        }

        tools.Note($"delete at restart {Path.GetRelativePath(tools.Volumes.Windows, path)}");
    }

    // Deletes what was marked, in order, like Windows does when it starts. A directory goes only once it's empty.
    public void DeleteMarkedAsWindowsStarts()
    {
        List<string> marked;

        lock (_lock)
        {
            marked = [.. _marked];
            _marked.Clear();
        }

        foreach (string path in marked)
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
            else if (Directory.Exists(path) && !Directory.EnumerateFileSystemEntries(path).Any())
            {
                Directory.Delete(path);
            }
        }
    }
}
