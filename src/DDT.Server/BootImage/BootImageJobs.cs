// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.BootImage;
using Microsoft.Extensions.Logging;

namespace DDT.Server.BootImage;

// Runs one job at a time through the helper and passes on what it writes. The request returns at once: a build takes
// minutes, and the page follows it over the hub.
public sealed partial class BootImageJobs(
    CurrentBootImageJob state,
    IBootImageHelper helper,
    BootImagePushes pushes,
    BootImageCatalog catalog,
    TimeProvider timeProvider,
    ILogger<BootImageJobs> logger)
{
    // Output is sent in batches, so a build that writes fast costs a page a few pushes a second.
    private static readonly TimeSpan s_outputInterval = TimeSpan.FromMilliseconds(300);

    // The job that was started last, for tests to wait on.
    public Task Last { get; private set; } = Task.CompletedTask;

    // Returns false while another job runs.
    public bool TryStart(BootImageJobKind kind, HelperRequest request, string startedBy)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!state.TryBegin(kind, startedBy, timeProvider.GetUtcNow()))
        {
            return false;
        }

        Last = Task.Run(() => RunAsync(kind, request));

        return true;
    }

    private async Task RunAsync(BootImageJobKind kind, HelperRequest request)
    {
        using CancellationTokenSource finished = new();
        Task output = PushOutputAsync(finished.Token);
        string? served = catalog.Current;
        string? problem;

        try
        {
            await pushes.ViewChangedAsync(CancellationToken.None).ConfigureAwait(false);
            problem = await RelayAsync(request).ConfigureAwait(false);

            // What was served until now stays, to go back to
            if (problem is null && kind == BootImageJobKind.Build)
            {
                catalog.Prune(served);
            }
        }
        catch (Exception exception)
        {
            // Reported on the page, where the job was started. A job must never take the host down.
            problem = exception is TimeoutException
                ? "The DDT Helper service does not answer. It has to run for the server to build on its own."
                : exception.Message;
            LogFailed(kind, exception);
        }

        await finished.CancelAsync().ConfigureAwait(false);
        await output.ConfigureAwait(false);
        state.End(problem, timeProvider.GetUtcNow());
        Flush();

        try
        {
            await pushes.ViewChangedAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            // The page then shows the end of the job at its next load.
            LogFailed(kind, exception);
        }
    }

    // Returns null when the helper ended with exit code 0, or what it gave as the reason.
    private async Task<string?> RelayAsync(HelperRequest request)
    {
        await foreach (HelperMessage message in helper.RunAsync(request, CancellationToken.None).ConfigureAwait(false))
        {
            if (message.Line is not null)
            {
                state.Append(message.Line);
            }

            if (message.ExitCode is { } exitCode)
            {
                return exitCode == 0 ? null : message.Problem ?? $"The helper ended with exit code {exitCode}.";
            }
        }

        return "The DDT Helper service closed the connection before the job was done.";
    }

    private async Task PushOutputAsync(CancellationToken finished)
    {
        using PeriodicTimer timer = new(s_outputInterval, timeProvider);

        try
        {
            while (await timer.WaitForNextTickAsync(finished).ConfigureAwait(false))
            {
                Flush();
            }
        }
        catch (OperationCanceledException)
        {
            // The job is over, and RunAsync sends what is left.
        }
    }

    private void Flush()
    {
        if (state.TakeUnpushed() is { } output)
        {
            pushes.Output(output);
        }
    }

    [LoggerMessage(EventId = 985, Level = LogLevel.Warning, Message = "The boot image job {Kind} failed")]
    private partial void LogFailed(BootImageJobKind kind, Exception exception);
}
