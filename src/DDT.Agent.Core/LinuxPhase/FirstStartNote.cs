// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Agent.Deployment;

namespace DDT.Agent.LinuxPhase;

// The start of Linux the agent first ran in, noted in the file at path. cloud-init acts on power_state at that start
// alone and keeps it in its configuration at every later one, where it announces nothing.
public sealed class FirstStartNote(ICloudInit cloudInit, string path, AgentLog log)
{
    private bool? _isLaterStart;

    // Notes this start's id unless one is noted. For the agent's first moment: on Ubuntu a power_state restart stops
    // the unit 0.4 s after its start.
    public void Take() => _isLaterStart ??= Compare();

    // Whether the note names another start of Linux than this one.
    public bool IsLaterStart => _isLaterStart ??= Compare();

    // Without an id, or a note that can be read, the start counts as the first, which only costs the wait.
    private bool Compare()
    {
        if (cloudInit.StartId() is not { } start)
        {
            return false;
        }

        try
        {
            if (File.Exists(path))
            {
                return File.ReadAllText(path).Trim() is { Length: > 0 } first && first != start;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");

            using StreamWriter note = new(new FileStream(path, OwnFile.Private(FileMode.Create)));
            note.Write(start);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            log.Warning($"The start of Linux the agent first ran in could not be noted in {path} ({exception.Message}).");
        }

        return false;
    }
}
