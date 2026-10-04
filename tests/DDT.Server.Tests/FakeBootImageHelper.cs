// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Runtime.CompilerServices;
using DDT.Server.BootImage;

namespace DDT.Server.Tests;

// Stands in for the DDT Helper service: records what the server asks, answers the lines a test gives it, and can
// hold a job open so a test sees it running.
public sealed class FakeBootImageHelper : IBootImageHelper
{
    public bool Available { get; set; } = true;

    public List<HelperRequest> Requests { get; } = [];

    public List<string> Lines { get; } = ["Copying Windows PE", "Adding the agent"];

    // Null ends with exit code 0.
    public string? Problem { get; set; }

    // A job waits for this before it ends. Completed by default.
    public TaskCompletionSource Gate { get; set; } = Completed();

    // What a real build would have left in the boot directory.
    public Action<HelperRequest>? OnDone { get; set; }

    public static TaskCompletionSource Completed()
    {
        TaskCompletionSource gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        gate.SetResult();

        return gate;
    }

    public async IAsyncEnumerable<HelperMessage> RunAsync(HelperRequest request, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        lock (Requests)
        {
            Requests.Add(request);
        }

        foreach (string line in Lines)
        {
            yield return new HelperMessage(Line: line);
        }

        await Gate.Task.WaitAsync(cancellationToken);

        if (Problem is null)
        {
            OnDone?.Invoke(request);
        }

        yield return new HelperMessage(ExitCode: Problem is null ? 0 : 1, Problem: Problem);
    }
}
