// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Agent.Deployment;

// The machine's current session and resume tokens during a run. Every report hands out fresh ones; the download and
// the unattend request read whatever is current.
public sealed class DeploymentTokens(string token, string resumeToken)
{
    private readonly Lock _lock = new();
    private TaskCompletionSource _changed = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private string _token = token;
    private string _resumeToken = resumeToken;

    public string Token
    {
        get
        {
            lock (_lock)
            {
                return _token;
            }
        }
    }

    public string ResumeToken
    {
        get
        {
            lock (_lock)
            {
                return _resumeToken;
            }
        }
    }

    public void Update(string newToken, string newResumeToken)
    {
        TaskCompletionSource changed;

        lock (_lock)
        {
            _token = newToken;
            _resumeToken = newResumeToken;
            changed = _changed;
            _changed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        changed.TrySetResult();
    }

    // A token other than the refused one, or null when none arrives in time.
    public async Task<string?> WaitForOtherThanAsync(string refused, TimeSpan timeout, TimeProvider timeProvider, CancellationToken cancellationToken)
    {
        using CancellationTokenSource waiting = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        Task deadline = Task.Delay(timeout, timeProvider, waiting.Token);

        try
        {
            while (true)
            {
                Task changed;

                lock (_lock)
                {
                    if (_token != refused)
                    {
                        return _token;
                    }

                    changed = _changed.Task;
                }

                if (await Task.WhenAny(changed, deadline).ConfigureAwait(false) == deadline)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    return null;
                }
            }
        }
        finally
        {
            await waiting.CancelAsync().ConfigureAwait(false);
        }
    }
}
