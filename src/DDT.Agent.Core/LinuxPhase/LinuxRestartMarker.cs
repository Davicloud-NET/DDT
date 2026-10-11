// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Agent.InstalledPhase;

namespace DDT.Agent.LinuxPhase;

// Keeps the due restart as a file under /run, a file system in memory, so every start of Linux begins without it. A
// marker that fails only costs the check.
public sealed class LinuxRestartMarker(string directory, AgentLog log) : IRestartMarker
{
    public const string FileName = "restart-due";

    public string FilePath => Path.Combine(directory, FileName);

    public bool IsSet => File.Exists(FilePath);

    public void Set()
    {
        try
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(FilePath, string.Empty);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            log.Warning($"The restart could not be recorded ({exception.Message}). Should the agent stop before Linux restarts, the run goes on without the restart.");
        }
    }
}
