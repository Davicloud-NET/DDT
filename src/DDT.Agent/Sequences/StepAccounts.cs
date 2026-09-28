// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Agent.Deployment;
using DDT.Contracts.Agents;
using DDT.Contracts.Sequences;
using DDT.Core.Sequences;

namespace DDT.Agent.Sequences;

// Sets up the accounts of a step around it: a step that lists shares or runs a script as an account reports that it
// runs, which the server needs before it hands out the step's accounts, fetches them, signs the run-as account in,
// connects the shares, in that account's logon session when there is one, and after the step, however it ended,
// disconnects the shares and signs the account out. The passwords stay in this call; no script sees them.
public sealed class StepAccounts(
    IAgentServer server,
    RunSession session,
    Func<CancellationToken, Task> reportRunning,
    AccountTools tools,
    AgentLog log,
    TimeProvider timeProvider)
{
    public const string RunAsInWindowsPE =
        "A script runs as an account only in Windows, after the hand-over, but this agent was asked to run one in Windows PE.";

    public const string NoRunAsAccount = "The server sent no account for this script to run as. Assign the sequence again.";

    public static bool Uses(SequenceStep step)
    {
        ArgumentNullException.ThrowIfNull(step);

        return step is RunScriptStep { RunAs: not null } || step.Shares is { Count: > 0 };
    }

    // run gets the signed-in account, or null for a step that runs as the agent.
    public async Task<StepResult> RunAsync(
        SequenceStep step,
        StepContext context,
        Func<IAccountSession?, Task<StepResult>> run,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(step);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(run);

        if (!Uses(step))
        {
            return await run(null).ConfigureAwait(false);
        }

        bool runsAs = step is RunScriptStep { RunAs: not null };

        if (runsAs && context.Phase == SequencePhase.WindowsPE)
        {
            return StepResult.Failed(RunAsInWindowsPE);
        }

        await reportRunning(cancellationToken).ConfigureAwait(false);

        AgentStepAccounts accounts = await ServerCallRules.CallAsync(
            call => server.GetRunStepAccountsAsync(session.MachineId, session.Tokens.Token, session.Run.Id, step.Id, call),
            "the step's accounts",
            log,
            timeProvider,
            cancellationToken).ConfigureAwait(false);

        IAccountSession? account = null;
        IAsyncDisposable? shares = null;

        try
        {
            if (runsAs)
            {
                AgentAccount runAs = accounts.RunAs ?? throw new DeploymentStepException(NoRunAsAccount);
                account = await tools.Logons.LogOnAsync(runAs, cancellationToken).ConfigureAwait(false);
            }

            shares = await tools.Shares.ConnectAsync(accounts.Shares ?? [], account, cancellationToken).ConfigureAwait(false);

            return await run(account).ConfigureAwait(false);
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
