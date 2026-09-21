// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Agent.Tests;

// Answers each read with the next typed line at once. Once the lines run out it waits, as a technician who
// has walked away does, until the read is cancelled.
internal sealed class ScriptedSignInPrompt(params string[] lines) : ISignInPrompt
{
    private readonly Queue<string> _lines = new(lines);

    public bool IsAvailable { get; init; } = true;

    public List<string> Labels { get; } = [];

    public int Cancelled { get; private set; }

    public Task<string?> ReadLineAsync(string label, bool secret, CancellationToken cancellationToken)
    {
        Labels.Add(secret ? $"{label} (hidden)" : label);

        if (_lines.TryDequeue(out string? line))
        {
            return Task.FromResult<string?>(line);
        }

        TaskCompletionSource<string?> waiting = new(TaskCreationOptions.RunContinuationsAsynchronously);
        cancellationToken.Register(() =>
        {
            Cancelled++;
            waiting.TrySetResult(null);
        });

        return waiting.Task;
    }
}
