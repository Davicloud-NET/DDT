// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Agent.Tests;

// What a test and the server that stands in for DDT's tell each other about a request the server keeps waiting: the
// server says when it has the request, and the test says when the answer may go out.
internal sealed class HeldAnswer
{
    private readonly TaskCompletionSource _asked = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _released = new(TaskCreationOptions.RunContinuationsAsynchronously);

    // Done once the server has read the whole request.
    public Task Asked => _asked.Task;

    public void Release() => _released.TrySetResult();

    // For the server: the request is in, and the answer waits for the test.
    public Task WaitAsync(CancellationToken cancellationToken)
    {
        _asked.TrySetResult();

        return _released.Task.WaitAsync(cancellationToken);
    }
}
