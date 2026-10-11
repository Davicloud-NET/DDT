// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Agent.Deployment;

namespace DDT.Agent.Sequences;

// Deletes what a step no longer needs. A file or directory that stays behind only costs space, so a failure is a
// warning and never fails the step. A link is deleted itself and never followed, whatever a script left behind.
internal static class Leftovers
{
    // A larger file is no token and no state of a run, and only deleted.
    internal const long MaxWipedBytes = 4L * 1024 * 1024;

    public static void Delete(string path, AgentLog log) => Delete(path, log, directory => MountsBelow.Holding(directory));

    // held says why a directory must stay with all it holds, or null.
    internal static void Delete(string path, AgentLog log, Func<string, string?> held)
    {
        try
        {
            if (Directory.Exists(path))
            {
                if (DeleteDirectory(path, held) is { } reason)
                {
                    log.Warning($"{path} was not deleted, because {reason}.");
                }
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

    // Null once the directory is gone with all it holds. Otherwise why it stays, as the end of a sentence: on Linux
    // a file system mounted in it would lose its files with it.
    public static string? DeleteDirectory(string path) => DeleteDirectory(path, directory => MountsBelow.Holding(directory));

    private static string? DeleteDirectory(string path, Func<string, string?> held)
    {
        // The link alone goes: what it points to is not the agent's
        if (new DirectoryInfo(path).LinkTarget is not null)
        {
            Directory.Delete(path);

            return null;
        }

        if (held(path) is { } reason)
        {
            return reason;
        }

        Directory.Delete(path, recursive: true);

        return null;
    }

    // For a file the agent wrote itself and that held a secret. Deleting alone leaves its bytes on the disk, so they
    // are overwritten first. Only OwnFile's kind of file is opened, so nothing a script put in its place is written to.
    public static void Wipe(string path, AgentLog log)
    {
        try
        {
            using FileStream? file = OwnFile.OpenToOverwrite(path);

            if (file is { Length: <= MaxWipedBytes })
            {
                file.Write(new byte[file.Length]);
                file.Flush(flushToDisk: true);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            log.Warning($"{path} could not be overwritten before it is deleted ({exception.Message}).");
        }

        Delete(path, log);
    }
}
