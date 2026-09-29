// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Agents;
using DDT.Contracts.Sequences;
using DDT.Core.Templates;
using DDT.Server.Accounts;
using DDT.Server.Data;
using DDT.Server.Machines;

namespace DDT.Server.Deployments;

// The accounts a step uses while it runs: the account a script in Windows runs as, and its shares. Each share only
// connects to a server its account lists. Every read is audited, and one refusal refuses them all.
public sealed class StepAccounts(DdtDbContext database, RunQueries queries, StepAccountLookup lookup, TimeProvider timeProvider)
{
    public async Task<(AgentStepAccounts? Accounts, string? Refusal)> ReadAsync(
        Machine machine,
        Guid runId,
        Guid stepId,
        string? address,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(machine);

        (RunningStep? running, string? refusal) = await queries.RunningStepAsync(machine, runId, stepId, cancellationToken).ConfigureAwait(false);

        if (running is null)
        {
            return (null, refusal);
        }

        if (running.Step is not { } step)
        {
            return (null, "That step uses no account.");
        }

        if (Refusal(machine, step) is { } refused)
        {
            return (null, refused);
        }

        Reads reads = new(lookup, running);
        AgentAccount? runAs = null;

        if (step is RunScriptStep { RunAs: { } reference })
        {
            (runAs, string? runAsRefused) = await RunAsAsync(reads, reference, step, cancellationToken).ConfigureAwait(false);

            if (runAsRefused is not null)
            {
                return (null, runAsRefused);
            }
        }

        (List<AgentShareConnection>? connections, string? shareRefused) = await SharesAsync(reads, machine, step, cancellationToken).ConfigureAwait(false);

        if (connections is null)
        {
            return (null, shareRefused);
        }

        foreach (string detail in reads.Audits)
        {
            database.AuditEvents.Add(RunSecrets.SecretRead(running.Run, machine, address, timeProvider.GetUtcNow(), detail));
        }

        return (new AgentStepAccounts(runAs, connections), null);
    }

    // Only a leaf step connects shares, and only a script in Windows runs as an account.
    private static string? Refusal(Machine machine, SequenceStep step)
    {
        if (step.IsContainer)
        {
            return "A group, an IF or a Repeat uses no account itself. Only the steps in it do.";
        }

        AccountReference? runAs = (step as RunScriptStep)?.RunAs;

        if (runAs is null && (step.Shares ?? []).Count == 0)
        {
            return "That step uses no account.";
        }

        if (runAs is not null && step is not RunScriptStep { Phase: SequencePhase.Windows })
        {
            return "Only a script in Windows runs as an account.";
        }

        return runAs is not null && machine.AgentEnvironment != AgentEnvironment.Windows
            ? "A script runs as an account in Windows, and this agent registered from Windows PE."
            : null;
    }

    private static async Task<(AgentAccount? Account, string? Refusal)> RunAsAsync(
        Reads reads,
        AccountReference reference,
        SequenceStep step,
        CancellationToken cancellationToken)
    {
        (StepAccount? account, string? refused) = await reads.ReadAsync(reference, cancellationToken).ConfigureAwait(false);

        if (account is null)
        {
            return (null, refused);
        }

        if (!account.RunAs)
        {
            return (null, $"The {account.Describe} does not let scripts run as it.");
        }

        reads.Audits.Add($"The {account.Describe} of step {step.Name} ({step.Id:D}) of {reads.Running.Run.Title}, to run the script as {account.UserName}.");

        return (new AgentAccount(account.UserName, account.Password), null);
    }

    // Each path is worked out from the values and the machine's facts the run started with, never from what the agent
    // reported since, so no step can choose where an account goes.
    private static async Task<(List<AgentShareConnection>? Connections, string? Refusal)> SharesAsync(
        Reads reads,
        Machine machine,
        SequenceStep step,
        CancellationToken cancellationToken)
    {
        Func<string, string?> values = ShareValues(machine, reads.Running);
        IReadOnlyList<ShareConnection?> shares = step.Shares ?? [];
        List<AgentShareConnection> connections = [];

        for (int index = 0; index < shares.Count; index++)
        {
            string which = $"share {index + 1} of {step.Name}";

            if (shares[index] is not { Account: { } reference, Path: { } written })
            {
                return (null, $"The {which} names no path or no account.");
            }

            if (!ValueTemplate.TryRender(written, values, out string path, out TemplateProblem? problem))
            {
                return (null, $"The path {written} of {which} cannot be worked out from the values the run started with. {problem?.Message().Text}");
            }

            if (AccountRules.ShareHost(path) is not { } host)
            {
                return (null, $@"{path}, the path of {which}, is not a share such as \\server\share.");
            }

            (StepAccount? account, string? refused) = await reads.ReadAsync(reference, cancellationToken).ConfigureAwait(false);

            if (account is null)
            {
                return (null, refused);
            }

            if (!AccountRules.Allows(account.Hosts, host))
            {
                return (null, $"The {account.Describe} may not connect to {host}, which the {which} names.");
            }

            connections.Add(new AgentShareConnection(path, account.UserName, account.Password));
            reads.Audits.Add($"The {account.Describe} of step {step.Name} ({step.Id:D}) of {reads.Running.Run.Title}, for {path}.");
        }

        return (connections, null);
    }

    // The machine's facts as the run started with them. A later registration, such as the service's in Windows, doesn't
    // change them. A run that started before its facts were kept uses the current ones.
    private static Func<string, string?> ShareValues(Machine machine, RunningStep running)
    {
        Func<string, string?> values = ValueTemplate.Lookup(RunValues.Effective(running.Run) ?? new Dictionary<string, string>());
        Func<string, string?> facts = ValueTemplate.Lookup(running.Inputs.Facts ?? RunInputs.FactsOf(machine));

        return name => values(name) ?? facts(name);
    }

    // Reads each account the step names once, and collects the audit detail for each use.
    private sealed class Reads(StepAccountLookup lookup, RunningStep running)
    {
        private readonly Dictionary<string, StepAccount> _read = new(StringComparer.Ordinal);

        public RunningStep Running => running;

        public List<string> Audits { get; } = [];

        public async Task<(StepAccount? Account, string? Refusal)> ReadAsync(AccountReference reference, CancellationToken cancellationToken)
        {
            string key = $"{reference.AccountId:D}|{reference.Input}";

            if (_read.TryGetValue(key, out StepAccount? known))
            {
                return (known, null);
            }

            (StepAccount? account, string? refused) = await lookup.ReadAsync(running.Run, running.Definition, reference, cancellationToken).ConfigureAwait(false);

            if (account is not null)
            {
                _read[key] = account;
            }

            return (account, refused);
        }
    }
}
