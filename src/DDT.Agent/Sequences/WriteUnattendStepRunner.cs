// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Agent.Deployment;
using DDT.Contracts.Sequences;
using DDT.Core.Sequences;

namespace DDT.Agent.Sequences;

// Writes the server's answer file into the applied Windows. It holds passwords, so the server renders it only while it
// knows the step runs, which reportRunning tells it first, and it is never logged: only what it sets, without secrets.
// Setup deletes it once Windows is installed, through the line UnattendFile puts first in SetupComplete.cmd.
public sealed class WriteUnattendStepRunner(
    IAgentServer server,
    RunSession session,
    Func<CancellationToken, Task> reportRunning,
    AgentLog log,
    TimeProvider timeProvider)
{
    public async Task<StepResult> RunAsync(WriteUnattendStep step, StepContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(step);
        ArgumentNullException.ThrowIfNull(context);

        TargetVolumes volumes = session.RequireVolumes();

        await reportRunning(cancellationToken).ConfigureAwait(false);

        string unattend = await ServerCallRules.CallAsync(
            call => server.GetRunUnattendAsync(session.MachineId, session.Tokens.Token, session.Run.Id, step.Id, call),
            "the answer file",
            log,
            timeProvider,
            cancellationToken).ConfigureAwait(false);

        string summary = UnattendFile.Summarize(unattend);

        try
        {
            await UnattendFile.WriteAsync(volumes.Windows, unattend, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            // Half written, it goes at once, whatever else happens to the run.
            Leftovers.Delete(UnattendFile.PathIn(volumes.Windows), log);

            throw;
        }

        log.Information($"Wrote the answer file: {summary}.");
        context.Progress.Report(100);

        return StepResult.Done();
    }
}
