// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Agent.Deployment;

namespace DDT.Agent.Sequences;

// Deletes what a step no longer needs. A file or directory that stays behind only costs space, so a failure is a
// warning and never fails the step. A link at the path is deleted itself, also one that points nowhere. A link in a
// folder above it is followed.
internal static class Leftovers
{
    // A larger file is no token and no state of a run. It is only deleted, which is said.
    internal const long MaxWipedBytes = 4L * 1024 * 1024;

    // The agent's log grows with what a run's scripts print. Overwriting more than this would hold the removal up.
    internal const long MaxWipedLogBytes = 256L * 1024 * 1024;

    private const int ZeroBytes = 64 * 1024;

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
            else
            {
                // Not asked first whether it exists: that follows a link, and one that points nowhere would stay
                File.Delete(path);
            }
        }
        catch (DirectoryNotFoundException)
        {
            // Not even its folder is there
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
    public static void Wipe(string path, AgentLog log) => Wipe(path, log, MaxWipedBytes);

    // For the agent's log, which may hold what only root should read and is far longer than a token.
    public static void WipeLog(string path, AgentLog log) => Wipe(path, log, MaxWipedLogBytes);

    // A file longer than the limit is deleted as it is, which is said: its bytes stay on the disk. The line names
    // the file and its size, never what it holds.
    private static void Wipe(string path, AgentLog log, long maxBytes)
    {
        try
        {
            using FileStream? file = OwnFile.OpenToOverwrite(path);

            if (file is not null && file.Length <= maxBytes)
            {
                Zero(file);
            }
            else if (file is not null)
            {
                log.Warning(
                    $"{path} holds {ByteSize.Format(file.Length)}, more than the {ByteSize.Format(maxBytes)} the agent overwrites before it " +
                    "deletes a file, so what it held stays on the disk until something else is written there.");
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            log.Warning($"{path} could not be overwritten before it is deleted ({exception.Message}).");
        }

        Delete(path, log);
    }

    private static void Zero(FileStream file)
    {
        byte[] zeros = new byte[(int)Math.Min(file.Length, ZeroBytes)];

        for (long left = file.Length; left > 0; left -= zeros.Length)
        {
            file.Write(zeros, 0, (int)Math.Min(left, zeros.Length));
        }

        file.Flush(flushToDisk: true);
    }
}
