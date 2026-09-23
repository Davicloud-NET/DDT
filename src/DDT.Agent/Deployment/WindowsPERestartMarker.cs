// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Agent.Sequences;

namespace DDT.Agent.Deployment;

// Keeps a restart that Windows PE owes the run as a file in the agent's directory on X:, the RAM disk that Windows PE
// builds anew from boot.wim at every start, so a restart clears it. A file is simpler than a volatile registry key,
// which a restart of Windows PE clears as well: it needs only the file system, holds where the restart leads as its
// text, and a test can keep it in a temporary folder. An agent stopped before the restart, or started again by hand
// after wpeutil failed, finds it and restarts instead of going on as if the restart had happened. A dry run starts its
// agent over for every restart, so the restarts it asks for always happen, and it only says what it would record. A
// marker that cannot be written or read only costs the check, so neither fails the run.
public sealed class WindowsPERestartMarker(string directory, AgentLog log, bool dryRun)
{
    public const string FileName = "restart-due";

    public string FilePath => Path.Combine(directory, FileName);

    // Where the due restart leads, or null when none is due. Anything but Windows leads back into Windows PE, which
    // then decides with the server.
    public RestartInto? Due
    {
        get
        {
            if (dryRun || !File.Exists(FilePath))
            {
                return null;
            }

            try
            {
                return File.ReadAllText(FilePath).Trim() == nameof(RestartInto.Windows) ? RestartInto.Windows : RestartInto.WindowsPE;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                log.Warning($"Whether the machine still has to restart for the run cannot be read ({exception.Message}).");

                return null;
            }
        }
    }

    public void Set(RestartInto into)
    {
        if (dryRun)
        {
            log.Information($@"Dry run: in Windows PE the due restart would be recorded in X:\DDT\{FileName}, on the RAM disk that the restart clears.");

            return;
        }

        try
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(FilePath, into == RestartInto.Windows ? nameof(RestartInto.Windows) : nameof(RestartInto.WindowsPE));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            log.Warning($"The restart could not be recorded ({exception.Message}). Should the agent stop before the machine restarts, the run goes on without the restart.");
        }
    }

    // The run gave up the restart.
    public void Clear()
    {
        if (!dryRun)
        {
            Leftovers.Delete(FilePath, log);
        }
    }
}
