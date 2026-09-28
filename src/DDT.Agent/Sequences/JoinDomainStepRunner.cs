// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Agent.Deployment;
using DDT.Contracts.Agents;
using DDT.Contracts.Sequences;
using DDT.Core.Sequences;

namespace DDT.Agent.Sequences;

// Joins the installed Windows to the server's domain, with the account the server hands out once reportRunning told it
// the step runs. The password never reaches a log or an error. The join takes effect at the restart the step asks for.
public sealed class JoinDomainStepRunner(
    IDomainJoiner joiner,
    IAgentServer server,
    RunSession session,
    Func<CancellationToken, Task> reportRunning,
    AgentLog log,
    TimeProvider timeProvider) : IStepKindRunner<JoinDomainStep>
{
    // While the network comes up after Windows started, no domain controller may answer yet.
    public static readonly TimeSpan RetryFor = TimeSpan.FromMinutes(5);

    public async Task<StepResult> RunAsync(JoinDomainStep step, StepContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(step);
        ArgumentNullException.ThrowIfNull(context);

        await reportRunning(cancellationToken).ConfigureAwait(false);

        AgentJoinDomainCredentials credentials = await ServerCallRules.CallAsync(
            call => server.GetRunJoinCredentialsAsync(session.MachineId, session.Tokens.Token, session.Run.Id, step.Id, call),
            "the domain join account",
            log,
            timeProvider,
            cancellationToken).ConfigureAwait(false);

        log.Information(credentials.OrganizationalUnit is { Length: > 0 } organizationalUnit
            ? $"Joining {credentials.Domain}, with the computer account in {organizationalUnit}."
            : $"Joining {credentials.Domain}.");

        long started = timeProvider.GetTimestamp();

        for (int failures = 1; ; failures++)
        {
            int code = await joiner.JoinAsync(credentials, cancellationToken).ConfigureAwait(false);

            if (code == 0)
            {
                log.Information($"Joined {credentials.Domain}.");
                context.Progress.Report(100);

                return StepResult.RebootRequired();
            }

            string problem = DomainJoinErrors.Describe(code, credentials.Domain, credentials.OrganizationalUnit);
            TimeSpan delay = AgentLimits.RetryDelay(failures);

            if (!DomainJoinErrors.IsTransient(code) || timeProvider.GetElapsedTime(started) + delay > RetryFor)
            {
                return StepResult.Failed(problem);
            }

            log.Warning($"{problem} Trying again in {delay.TotalSeconds:0} s.");
            await Task.Delay(delay, timeProvider, cancellationToken).ConfigureAwait(false);
        }
    }
}
