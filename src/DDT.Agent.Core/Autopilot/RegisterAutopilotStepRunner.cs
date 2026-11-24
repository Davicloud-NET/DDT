// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Agent.Deployment;
using DDT.Agent.Sequences;
using DDT.Contracts.Agents;
using DDT.Contracts.Machines;
using DDT.Contracts.Sequences;
using DDT.Core.Sequences;

namespace DDT.Agent.Autopilot;

// Reads the hardware hash once and hands it to the server, which uploads it to Autopilot. With WaitForImport the step
// asks the server until Autopilot has imported it. Never logs the hash.
public sealed class RegisterAutopilotStepRunner(
    IAgentServer server,
    RunSession session,
    Func<CancellationToken, Task> reportRunning,
    IHardwareHashReader reader,
    AgentLog log,
    TimeProvider timeProvider) : IStepKindRunner<RegisterAutopilotStep>
{
    // As often as the server follows the import.
    public static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(30);

    public const string NoUpload = "The server is not set up to upload to Autopilot, so the step does not wait. Export the CSV from the machine's page.";

    public static string NotClient(string productType) =>
        $"Autopilot takes only Windows client editions, and this Windows is a server ({productType}). No hash was read.";

    public async Task<StepResult> RunAsync(RegisterAutopilotStep step, StepContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(step);
        ArgumentNullException.ThrowIfNull(context);

        // The server takes the hash only while it knows this step is running.
        await reportRunning(cancellationToken).ConfigureAwait(false);

        HardwareHashReading reading = await reader.ReadAsync(cancellationToken).ConfigureAwait(false);

        if (!reading.IsClient)
        {
            return StepResult.Failed(NotClient(reading.ProductType));
        }

        string? groupTag = string.IsNullOrWhiteSpace(step.GroupTag) ? null : step.GroupTag.Trim();
        await ServerCallRules.CallAsync(
            async call =>
            {
                await server.SendRunRecordAsync(session.MachineId, session.Tokens.Token, session.Run.Id, new AgentHardwareHash(reading.SerialNumber, reading.Hash, groupTag), call)
                    .ConfigureAwait(false);

                return true;
            },
            "the hardware hash",
            log,
            timeProvider,
            cancellationToken).ConfigureAwait(false);
        log.Information($"Handed the hardware hash of serial number {reading.SerialNumber} to the server.");

        if (!step.WaitForImport)
        {
            context.Progress.Report(100);

            return StepResult.Done();
        }

        context.Progress.Report(50);

        return await WaitAsync(step, context, cancellationToken).ConfigureAwait(false);
    }

    private async Task<StepResult> WaitAsync(RegisterAutopilotStep step, StepContext context, CancellationToken cancellationToken)
    {
        int polls = (int)Math.Ceiling(TimeSpan.FromMinutes(step.WaitMinutes) / PollInterval);

        for (int poll = 1; ; poll++)
        {
            AgentHardwareHashStatus status = await ServerCallRules.CallAsync(
                call => server.GetRunHardwareHashStatusAsync(session.MachineId, session.Tokens.Token, session.Run.Id, call),
                "the Autopilot import",
                log,
                timeProvider,
                cancellationToken).ConfigureAwait(false);

            switch (status.State)
            {
                case HardwareHashUploadState.Imported:
                    context.Progress.Report(100);
                    log.Information("Autopilot imported the hardware hash.");

                    return StepResult.Done();
                case HardwareHashUploadState.Failed:
                    return StepResult.Failed($"Autopilot did not import the hardware hash: {status.Message ?? "no reason given"}");
                case HardwareHashUploadState.None:
                    log.Warning(NoUpload);
                    context.Progress.Report(100);

                    return StepResult.Done();
            }

            if (poll > polls)
            {
                return StepResult.Failed($"Autopilot had not imported the hardware hash after {step.WaitMinutes} minutes.");
            }

            await Task.Delay(PollInterval, timeProvider, cancellationToken).ConfigureAwait(false);
        }
    }
}
