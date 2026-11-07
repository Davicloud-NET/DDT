// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Agent.Sequences;

namespace DDT.Agent.Deployment;

// A restart the boot environment still owes the run, as a file where every start begins empty. Never fails the run.
public sealed class FileRestartMarker(string directory, AgentLog log, bool dryRun) : IBootRestartMarker
{
    public const string FileName = "restart-due";

    // The words in the file. An agent of another version reads them too, so they stay.
    private const string InstalledSystemWord = "Windows";
    private const string BootEnvironmentWord = "WindowsPE";

    // What a dry run says in place of recording the restart. Windows PE's words unless the composition gives its own.
    public string DryRunNote { get; init; } =
        $@"in Windows PE the due restart would be recorded in X:\DDT\{FileName}, on the RAM disk that the restart clears";

    public string FilePath => Path.Combine(directory, FileName);

    // Where the due restart leads, or null when none is due. Anything but the installed system's word leads back into
    // the boot environment, which then decides with the server.
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
                return File.ReadAllText(FilePath).Trim() == InstalledSystemWord ? RestartInto.InstalledSystem : RestartInto.BootEnvironment;
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
        // A dry run starts its agent over for every restart, so the restarts it asks for always happen.
        if (dryRun)
        {
            log.Information($"Dry run: {DryRunNote}.");

            return;
        }

        try
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(FilePath, into == RestartInto.InstalledSystem ? InstalledSystemWord : BootEnvironmentWord);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            log.Warning($"The restart could not be recorded ({exception.Message}). Should the agent stop before the machine restarts, the run goes on without the restart.");
        }
    }

    public void Clear()
    {
        if (!dryRun)
        {
            Leftovers.Delete(FilePath, log);
        }
    }
}
