// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace DDT.Agent.WindowsUpdates;

// A Begin* job polled until IsCompleted, then End*. A stop asks for RequestAbort. A search or a download ends within a
// second of it and throws; an installation may not stop at all, so it is waited for and answers.
internal sealed class UpdateJob(AutomationObject owner, string kind, CancellationToken disposing)
{
    public static readonly TimeSpan Poll = TimeSpan.FromMilliseconds(500);

    // Reports the job's percent while it runs. Null for a search, which has none.
    public IProgress<int>? Percent { get; init; }

    // An installation ignores the stop past asking for the abort.
    public bool WaitsAfterAbort { get; init; }

    // Criteria only for a search.
    public AutomationObject Run(CancellationToken stop, string? criteria = null)
    {
        stop.ThrowIfCancellationRequested();

        JobCallback callback = new();
        nint unknown = callback.Unknown();

        try
        {
            using AutomationObject job = Begin(unknown, criteria);
            bool stopped = Wait(job, stop) && !WaitsAfterAbort;
            AutomationObject result;

            try
            {
                result = owner.CallForObject("End" + kind, AutomationValues.Dispatch(job.Pointer));
            }
            catch (ComCallException) when (stopped)
            {
                CleanUp(job);

                throw new OperationCanceledException(stop);
            }

            CleanUp(job);

            if (stopped)
            {
                result.Dispose();

                throw new OperationCanceledException(stop);
            }

            return result;
        }
        finally
        {
            Marshal.Release(unknown);
        }
    }

    // The job has ended; a failure to release its callbacks changes nothing.
    private static void CleanUp(AutomationObject job)
    {
        try
        {
            job.Call("CleanUp")?.Dispose();
        }
        catch (ComCallException)
        {
        }
    }

    // A search takes the criteria and one callback; a download or an installation takes two.
    private AutomationObject Begin(nint callback, string? criteria)
    {
        ComVariant state = default;

        return criteria is null
            ? owner.CallForObject("Begin" + kind, AutomationValues.Unknown(callback), AutomationValues.Unknown(callback), state)
            : owner.CallForObject("Begin" + kind, AutomationValues.Text(criteria), AutomationValues.Unknown(callback), state);
    }

    // True when the job was asked to abort. Disposal leaves the job running: CleanUp would wait for it.
    private bool Wait(AutomationObject job, CancellationToken stop)
    {
        bool aborted = false;
        int reported = -1;

        while (!job.GetBool("IsCompleted"))
        {
            if (disposing.IsCancellationRequested)
            {
                throw new OperationCanceledException(disposing);
            }

            if (stop.IsCancellationRequested && !aborted)
            {
                job.Call("RequestAbort")?.Dispose();
                aborted = true;
            }

            reported = Report(job, reported);
            WaitHandle.WaitAny(aborted ? [disposing.WaitHandle] : [stop.WaitHandle, disposing.WaitHandle], Poll);
        }

        Report(job, reported);

        return aborted;
    }

    // The percent is for the console only, so a failed read is skipped.
    private int Report(AutomationObject job, int reported)
    {
        if (Percent is null)
        {
            return reported;
        }

        try
        {
            using AutomationObject progress = job.CallForObject("GetProgress");
            int percent = Math.Clamp(progress.GetInt("PercentComplete"), 0, 100);

            if (percent != reported)
            {
                Percent.Report(percent);
            }

            return percent;
        }
        catch (ComCallException)
        {
            return reported;
        }
    }
}
