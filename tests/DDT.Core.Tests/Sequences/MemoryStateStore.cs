// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Sequences;
using DDT.Core.Sequences;

namespace DDT.Core.Tests.Sequences;

// Keeps the saved states as they are, for step kinds the JSON contexts do not know.
public sealed class MemoryStateStore : ISequenceStateStore
{
    private readonly List<SequenceState> _states = [];

    public IReadOnlyList<SequenceState> States => _states;

    public Task SaveAsync(SequenceState state, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        _states.Add(state);

        return Task.CompletedTask;
    }
}
