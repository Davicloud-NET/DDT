// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Agent.Sequences;

// Deletes what a step no longer needs. A file or directory that stays behind only costs space, so a failure is a
// warning and never fails the step.
internal static class Leftovers
{
    public static void Delete(string path, AgentLog log)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
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
}
