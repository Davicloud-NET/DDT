// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Text.Json;
using DDT.Contracts.Agents;
using DDT.Contracts.Sequences;
using DDT.Core.Sequences;

namespace DDT.Core.Tests.Sequences;

// Keeps every saved state as the JSON the agent writes to disk, so a test can resume from any of them.
public sealed class BlobStore : ISequenceStateStore
{
    private readonly List<string> _blobs = [];

    public IReadOnlyList<string> Blobs => _blobs;

    public string Latest => _blobs[^1];

    public IReadOnlyList<SequenceState> States => [.. _blobs.Select(Load)];

    public Func<SequenceState, bool> FailWhen { get; set; } = _ => false;

    public static SequenceState Load(string blob) =>
        JsonSerializer.Deserialize(blob, AgentJsonContext.Default.SequenceState)
        ?? throw new InvalidOperationException("The blob holds no state.");

    public Task SaveAsync(SequenceState state, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (FailWhen(state))
        {
            throw new IOException("There is not enough space on the disk.");
        }

        _blobs.Add(JsonSerializer.Serialize(state, AgentJsonContext.Default.SequenceState));

        return Task.CompletedTask;
    }
}
