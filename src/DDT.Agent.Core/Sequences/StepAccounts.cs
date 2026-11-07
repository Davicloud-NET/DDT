// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Agent.Deployment;
using DDT.Contracts.Agents;
using DDT.Contracts.Sequences;
using DDT.Core.Sequences;

namespace DDT.Agent.Sequences;

// Sets up a step's accounts around it. It signs the run-as account in and connects the shares, in that account's logon
// session when there is one. Both are undone after the step, however it ended. No script sees the passwords.
public sealed class StepAccounts(
    IAgentServer server,
    RunSession session,
    Func<CancellationToken, Task> reportRunning,
    AccountTools tools,
    AgentLog log,
    TimeProvider timeProvider)
{
    public const string NoRunAsAccount = "The server sent no account for this script to run as. Assign the sequence again.";

    public static string RunAsInBoot(SequencePhase phase) =>
        $"A script runs as an account only in Windows, after the hand-over, but this agent was asked to run one in {AgentEnvironments.OfBootPhase(phase)}.";

    public static bool Uses(SequenceStep step)
    {
        ArgumentNullException.ThrowIfNull(step);

        return step is RunScriptStep { RunAs: not null } || step.Shares is { Count: > 0 };
    }

    // run gets the signed-in account and the shares' places, or nothing for a step without accounts.
    public async Task<StepResult> RunAsync(
        SequenceStep step,
        StepContext context,
        Func<StepResources, Task<StepResult>> run,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(step);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(run);

        if (!Uses(step))
        {
            return await run(StepResources.None).ConfigureAwait(false);
        }

        bool runsAs = step is RunScriptStep { RunAs: not null };

        if (runsAs && SequenceKinds.IsBoot(context.Phase))
        {
            return StepResult.Failed(RunAsInBoot(context.Phase));
        }

        // The server only hands out a step's accounts once it knows the step is running.
        await reportRunning(cancellationToken).ConfigureAwait(false);

        AgentStepAccounts accounts = await ServerCallRules.CallAsync(
            call => server.GetRunStepAccountsAsync(session.MachineId, session.Tokens.Token, session.Run.Id, step.Id, call),
            "the step's accounts",
            log,
            timeProvider,
            cancellationToken).ConfigureAwait(false);

        IAccountSession? account = null;
        IConnectedShares? shares = null;

        try
        {
            if (runsAs)
            {
                AgentAccount runAs = accounts.RunAs ?? throw new DeploymentStepException(NoRunAsAccount);
                account = await tools.Logons.LogOnAsync(runAs, cancellationToken).ConfigureAwait(false);
            }

            shares = await tools.Shares.ConnectAsync(accounts.Shares ?? [], account, cancellationToken).ConfigureAwait(false);

            return await run(new StepResources(account, shares.LocalPaths)).ConfigureAwait(false);
        }
        finally
        {
            if (shares is not null)
            {
                await shares.DisposeAsync().ConfigureAwait(false);
            }

            account?.Dispose();
        }
    }
}
