// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Collections.Concurrent;

namespace DDT.Agent.WindowsUpdates;

// One MTA thread that runs work items in order. COM objects made on it are used and released only on it; ending runs
// there last, before the thread leaves COM.
public sealed class ComThread : IDisposable
{
    // A COM call that hangs must not hang the agent: the thread is a background one, and Dispose stops waiting.
    public static readonly TimeSpan JoinLimit = TimeSpan.FromMinutes(1);

    private readonly BlockingCollection<Action> _work = [];
    private readonly CancellationTokenSource _disposing = new();
    private readonly Thread _thread;
    private readonly Action _ending;
    private readonly Lock _lock = new();
    private bool _disposed;

    public ComThread(string name, Action ending)
    {
        ArgumentNullException.ThrowIfNull(ending);

        _ending = ending;
        Disposing = _disposing.Token;
        _thread = new Thread(Loop) { IsBackground = true, Name = name };

        // The completed callbacks of Begin* never come in an STA.
        _thread.SetApartmentState(ApartmentState.MTA);
        _thread.Start();
    }

    // Cancelled once Dispose starts: a running item stops waiting on Windows Update.
    public CancellationToken Disposing { get; }

    public bool IsAlive => _thread.IsAlive;

    public Task<T> RunAsync<T>(Func<T> work)
    {
        ArgumentNullException.ThrowIfNull(work);

        TaskCompletionSource<T> done = new(TaskCreationOptions.RunContinuationsAsynchronously);

        lock (_lock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _work.Add(() => Run(work, done));
        }

        return done.Task;
    }

    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _work.CompleteAdding();
        }

        _disposing.Cancel();

        if (Thread.CurrentThread != _thread && _thread.Join(JoinLimit))
        {
            _work.Dispose();
            _disposing.Dispose();
        }
    }

    // The work's failure goes to the caller, on whatever thread awaits it.
    private void Run<T>(Func<T> work, TaskCompletionSource<T> done)
    {
        if (_disposing.IsCancellationRequested)
        {
            done.TrySetException(new ObjectDisposedException(nameof(ComThread)));

            return;
        }

        try
        {
            done.TrySetResult(work());
        }
        catch (Exception exception)
        {
            done.TrySetException(exception);
        }
    }

    private void Loop()
    {
        int entered = ComNativeMethods.CoInitializeEx(0, ComNativeMethods.MultiThreaded);

        try
        {
            foreach (Action item in _work.GetConsumingEnumerable())
            {
                item();
            }
        }
        finally
        {
            _ending();

            if (entered >= 0)
            {
                ComNativeMethods.CoUninitialize();
            }
        }
    }
}
