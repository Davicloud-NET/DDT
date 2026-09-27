// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Deployments;
using DDT.Contracts.Messages;
using DDT.Contracts.Rules;
using DDT.Server.Data;
using DDT.Server.Deployments;
using DDT.Server.Machines;
using DDT.Server.Sequences;
using Microsoft.EntityFrameworkCore;

namespace DDT.Server.Rules;

// The sequence a machine gets, first match first: an assignment on the web, a choice at the machine, a rule for one
// of its MAC addresses, a rule for its model. Resolving changes nothing and authorizes nothing: an approval on the web
// runs a rule's sequence, and a console only offers it.
public sealed class SequenceResolver(DdtDbContext database)
{
    public async Task<SequenceResolution> ResolveAsync(Machine machine, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(machine);

        if (machine.ActiveDeploymentId is { } activeId
            && await database.Deployments.AsNoTracking().FirstOrDefaultAsync(d => d.Id == activeId, cancellationToken).ConfigureAwait(false)
                is { } active)
        {
            object by = active.RequestedByName ?? (object)ServerMessages.SomeOperator.With();

            return active.Source == DeploymentSource.Console
                ? new SequenceResolution(
                    SequenceResolutionSource.Console,
                    null,
                    null,
                    active,
                    ServerMessages.ResolutionChosenAtMachine.With("by", by, "sequence", active.Title))
                : new SequenceResolution(
                    SequenceResolutionSource.Assigned,
                    null,
                    null,
                    active,
                    ServerMessages.ResolutionAssignedOnWeb.With("by", by, "sequence", active.Title));
        }

        List<AssignmentRule> rules = await database.AssignmentRules.AsNoTracking().ToListAsync(cancellationToken).ConfigureAwait(false);

        if ((MacRule(rules, machine) ?? ModelRule(rules, machine)) is not { } rule)
        {
            return new SequenceResolution(
                SequenceResolutionSource.None,
                null,
                null,
                null,
                ServerMessages.ResolutionNoRule.With());
        }

        TaskSequence sequence = await database.TaskSequences
            .AsNoTracking()
            .FirstAsync(s => s.Id == rule.TaskSequenceId, cancellationToken)
            .ConfigureAwait(false);

        return new SequenceResolution(
            rule.Kind == AssignmentRuleKind.Mac ? SequenceResolutionSource.MacRule : SequenceResolutionSource.ModelRule,
            sequence,
            rule,
            null,
            ServerMessages.ResolutionRuleChooses.With("rule", AssignmentRuleKeys.DescribeMessage(rule), "sequence", sequence.Name));
    }

    // The primary MAC address first, then the others in the order the machine reported them.
    private static AssignmentRule? MacRule(List<AssignmentRule> rules, Machine machine)
    {
        Dictionary<string, AssignmentRule> byMac = rules
            .Where(r => r.Kind == AssignmentRuleKind.Mac && r.Mac is not null)
            .ToDictionary(r => r.Mac!, StringComparer.Ordinal);
        string[] macs = [machine.PrimaryMac, .. machine.MacAddresses.Split(',', StringSplitOptions.RemoveEmptyEntries)];

        return macs.Select(mac => byMac.GetValueOrDefault(mac)).FirstOrDefault(rule => rule is not null);
    }

    // The exact model before a prefix, the longest prefix first, and a rule for the machine's maker before one for
    // any maker.
    private static AssignmentRule? ModelRule(List<AssignmentRule> rules, Machine machine) =>
        rules
            .Where(r => r.Kind == AssignmentRuleKind.Model
                && HardwareModels.Matches(r.Manufacturer, machine.Manufacturer)
                && HardwareModels.Matches(r.Model, machine.Model))
            .OrderBy(r => HardwareModels.IsPrefix(r.Model))
            .ThenByDescending(r => r.Model?.Length ?? 0)
            .ThenBy(r => r.Manufacturer is null)
            .ThenBy(r => r.Id)
            .FirstOrDefault();
}
