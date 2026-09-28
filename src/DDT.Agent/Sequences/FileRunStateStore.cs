// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Agent.Deployment;
using DDT.Contracts.Sequences;
using DDT.Core.Sequences;

namespace DDT.Agent.Sequences;

// The engine's store: in memory until Partition gives the run its directory, then every state and each new run token go
// to the disk before saved sees the state, so the server never hears of a state the disk does not have.
public sealed class FileRunStateStore(DeploymentTokens tokens, Action<SequenceState>? saved = null) : ISequenceStateStore
{
    // The heartbeat writes a new run token from its own thread.
    private readonly SemaphoreSlim _writing = new(1, 1);
    private RunFiles? _files;
    private SequenceState? _state;
    private string? _writtenToken;

    // The last state saved; null before the first.
    public SequenceState? State => _state;

    // Null until the run has its directory.
    public RunFiles? Files => _files;

    // From Partition on, or at once for a run the agent found on the disk after a restart. Writes what it holds now.
    public async Task AttachAsync(RunFiles files, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(files);

        await _writing.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            _files = files;
            _writtenToken = null;

            if (_state is { } state)
            {
                await files.SaveStateAsync(state, cancellationToken).ConfigureAwait(false);
            }

            await WriteTokenAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _writing.Release();
        }
    }

    public async Task SaveAsync(SequenceState state, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(state);

        await _writing.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            _state = state;

            if (_files is { } files)
            {
                await files.SaveStateAsync(state, cancellationToken).ConfigureAwait(false);
                await WriteTokenAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            _writing.Release();
        }

        saved?.Invoke(state);
    }

    // Writes the newest run token when it is not the one on the disk yet, as every save does, and before a restart.
    public async Task SaveTokenAsync(CancellationToken cancellationToken)
    {
        await _writing.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            await WriteTokenAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _writing.Release();
        }
    }

    private async Task WriteTokenAsync(CancellationToken cancellationToken)
    {
        if (_files is not { } files || tokens.RunToken is not { } token || token == _writtenToken)
        {
            return;
        }

        await files.SaveTokenAsync(token, cancellationToken).ConfigureAwait(false);
        _writtenToken = token;
    }
}
