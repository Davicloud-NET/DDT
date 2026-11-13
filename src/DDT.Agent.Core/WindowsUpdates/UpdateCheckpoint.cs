// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Text.Json;

namespace DDT.Agent.WindowsUpdates;

// What a Windows Update step did over its turns, by update id: the content of its checkpoint. Names carry the KB numbers.
public sealed class UpdateCheckpoint
{
    public Dictionary<string, string> Installed { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, FailedUpdate> Failed { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    // A checkpoint that cannot be read counts as none.
    public static UpdateCheckpoint Read(string? saved)
    {
        UpdateCheckpoint? read = null;

        try
        {
            read = string.IsNullOrEmpty(saved) ? null : JsonSerializer.Deserialize(saved, UpdateCheckpointJsonContext.Default.UpdateCheckpoint);
        }
        catch (JsonException)
        {
        }

        return new UpdateCheckpoint
        {
            Installed = new(read?.Installed ?? [], StringComparer.OrdinalIgnoreCase),
            Failed = new(read?.Failed ?? [], StringComparer.OrdinalIgnoreCase),
        };
    }

    // Failed twice: it is not tried again.
    public bool GaveUp(string id) => Failed.TryGetValue(id, out FailedUpdate? failed) && failed.Tries >= 2;

    public void Succeeded(string id, string name)
    {
        Installed[id] = name;
        Failed.Remove(id);
    }

    public void Failure(string id, string name, int hResult) =>
        Failed[id] = new FailedUpdate(name, (Failed.TryGetValue(id, out FailedUpdate? before) ? before.Tries : 0) + 1, hResult);

    public string Write() => JsonSerializer.Serialize(this, UpdateCheckpointJsonContext.Default.UpdateCheckpoint);
}
