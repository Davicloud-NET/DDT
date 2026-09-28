// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Agent.Deployment;
using DDT.Contracts.Sequences;
using DDT.Core.Sequences;

namespace DDT.Agent.Sequences;

// Writes the server's answer file, which holds passwords, into the applied Windows. The server only renders it once
// reportRunning has told it the step is running, and it's only logged as a summary. Setup deletes it through a line in
// SetupComplete.cmd.
public sealed class WriteUnattendStepRunner(
    IAgentServer server,
    RunSession session,
    Func<CancellationToken, Task> reportRunning,
    AgentLog log,
    TimeProvider timeProvider) : IStepKindRunner<WriteUnattendStep>
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
            // A half-written file is deleted right away, whatever else happens to the run.
            Leftovers.Delete(UnattendFile.PathIn(volumes.Windows), log);

            throw;
        }

        log.Information($"Wrote the answer file: {summary}.");
        context.Progress.Report(100);

        return StepResult.Done();
    }
}
