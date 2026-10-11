// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Agent.Deployment;

namespace DDT.Agent.Sequences;

// Deletes what a step no longer needs. A file or directory that stays behind only costs space, so a failure is a
// warning and never fails the step.
internal static class Leftovers
{
    // A larger file is no secret of a run, and only deleted.
    private const long MaxWipedBytes = 4L * 1024 * 1024;

    public static void Delete(string path, AgentLog log)
    {
        try
        {
            if (Directory.Exists(path))
            {
                DeleteDirectory(path, log);
            }
            else if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            log.Warning($"{path} could not be deleted ({exception.Message}).");
        }
    }

    // For a file that held a secret. Deleting alone leaves its bytes on the disk, so they are overwritten first.
    public static void Wipe(string path, AgentLog log)
    {
        try
        {
            if (File.Exists(path))
            {
                using FileStream file = new(path, FileMode.Open, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);

                if (file.Length <= MaxWipedBytes)
                {
                    file.Write(new byte[file.Length]);
                    file.Flush(flushToDisk: true);
                }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            log.Warning($"{path} could not be overwritten before it is deleted ({exception.Message}).");
        }

        Delete(path, log);
    }

    // Every file below the directory as Wipe leaves it, then the directory.
    public static void WipeAll(string directory, AgentLog log)
    {
        try
        {
            if (Directory.Exists(directory) && MountsBelow.Of(Path.GetFullPath(directory)).Count == 0)
            {
                foreach (string file in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
                {
                    Wipe(file, log);
                }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            log.Warning($"{directory} could not be read to overwrite what it holds ({exception.Message}).");
        }

        Delete(directory, log);
    }

    // On Linux a file system mounted in the directory would lose its files with it, so such a directory stays.
    private static void DeleteDirectory(string path, AgentLog log)
    {
        if (MountsBelow.Of(Path.GetFullPath(path)) is { Count: > 0 } mounted)
        {
            log.Warning($"{path} was not deleted, because {string.Join(", ", mounted)} is still mounted in it and would lose its files.");

            return;
        }

        Directory.Delete(path, recursive: true);
    }
}
