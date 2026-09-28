// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Agent.Deployment;

// Runs a blocking Windows call on a thread that exists for it alone, so it holds up no thread of the pool, and so a
// thread that acts as another account ends with the call and can never carry that account into other work.
public static class DedicatedThread
{
    public static Task<T> RunAsync<T>(Func<T> action, string name)
    {
        ArgumentNullException.ThrowIfNull(action);

        TaskCompletionSource<T> result = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Thread thread = new(() =>
        {
            try
            {
                result.SetResult(action());
            }
            catch (Exception exception)
            {
                result.SetException(exception);
            }
        })
        {
            IsBackground = true,
            Name = name,
        };

        thread.Start();

        return result.Task;
    }
}
