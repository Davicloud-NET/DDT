// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Agent.Deployment;

// The machine's session and resume tokens, which every answer renews, and the run token, which resumes a run after a
// restart. The server does not rotate the run token, so the newest one it sent is kept.
public sealed class DeploymentTokens(string token, string resumeToken, string? runToken = null)
{
    private readonly Lock _lock = new();
    private TaskCompletionSource _changed = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private string _token = token;
    private string _resumeToken = resumeToken;
    private string? _runToken = runToken;

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

    // Null until the server issued one.
    public string? RunToken
    {
        get
        {
            lock (_lock)
            {
                return _runToken;
            }
        }
    }

    // A null newRunToken keeps the run token there is: an answer without one does not end the run.
    public void Update(string newToken, string newResumeToken, string? newRunToken = null)
    {
        TaskCompletionSource changed;

        lock (_lock)
        {
            _token = newToken;
            _resumeToken = newResumeToken;
            _runToken = newRunToken ?? _runToken;
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
