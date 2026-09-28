// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Sequences;
using DDT.Contracts.Values;
using DDT.Core.Sequences;
using DDT.Core.Values;
using DDT.Server.Deployments;
using DDT.Server.Machines;

namespace DDT.Server.Rules;

// The values a run on a machine works with, from the sources ValueSources lists in the order they win: the answers to
// the sequence's inputs, the machine's own values, the rules that match it from the top, the machine roles in the order
// those rules give them, the sequence's defaults, and the deployment defaults. The rules are walked again each time, so
// a rule changed after a run was assigned counts when it starts.
//
// The machine page previews them through GET /api/machines/{id}/sequence. A run captures them when it starts, with
//
//     ValueResolution values = await machineValues.ResolveAsync(machine, definition, answers, snapshot.Deployment, cancellationToken);
//
// where definition is the run's frozen sequence, answers its answers by input name (RunAnswer.Read and Answers), and
// snapshot the SettingsSnapshot its start is checked against, so a save between the check and the capture cannot start a
// run with values nobody checked. values.Problems keep it from starting (ProblemsOf says them as a page does);
// values.Values are what the run stores, and values.Effective what its templates read.
public sealed class MachineValues(SequenceResolver resolver)
{
    // The names the deployment defaults give their values, as RunInputs takes them from the Deployment defaults page.
    public const string TimeZone = "TimeZone";
    public const string Locale = "Locale";
    public const string Keyboard = "Keyboard";
    public const string OrganizationalUnit = "OrganizationalUnit";
    public const string AdministratorName = "AdministratorName";

    public async Task<ValueResolution> ResolveAsync(
        Machine machine,
        SequenceDefinition? sequence,
        IReadOnlyDictionary<string, string>? answers,
        DeploymentOptions deployment,
        CancellationToken cancellationToken) =>
        ValueResolver.Resolve(await SourcesAsync(machine, sequence, answers, deployment, cancellationToken).ConfigureAwait(false));

    public async Task<ValueSources> SourcesAsync(
        Machine machine,
        SequenceDefinition? sequence,
        IReadOnlyDictionary<string, string>? answers,
        DeploymentOptions deployment,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(machine);
        ArgumentNullException.ThrowIfNull(deployment);

        SequenceResolution resolution = await resolver.ResolveAsync(machine, cancellationToken).ConfigureAwait(false);

        return Sources(machine, resolution.Machine, resolution.Match, sequence, answers, deployment);
    }

    // For a caller that walked the rules already, such as the resolver's.
    public static ValueSources Sources(
        Machine machine,
        MachineVariables facts,
        RuleMatch match,
        SequenceDefinition? sequence,
        IReadOnlyDictionary<string, string>? answers,
        DeploymentOptions deployment)
    {
        ArgumentNullException.ThrowIfNull(machine);
        ArgumentNullException.ThrowIfNull(facts);
        ArgumentNullException.ThrowIfNull(match);
        ArgumentNullException.ThrowIfNull(deployment);

        return new ValueSources
        {
            Sequence = sequence,
            Answers = answers ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            Machine = Own(machine),
            Rules = match.RuleValues,
            Roles = match.RoleValues,
            DeploymentDefaults = DeploymentDefaults(deployment),
            Facts = facts,
        };
    }

    // The machine's own values: the name an operator or the technician gave it is its ComputerName.
    public static IReadOnlyList<NamedValue> Own(Machine machine)
    {
        ArgumentNullException.ThrowIfNull(machine);

        return string.IsNullOrWhiteSpace(machine.AssignedName)
            ? []
            : [new NamedValue(MachineVariableNames.ComputerName, machine.AssignedName.Trim())];
    }

    // What the Deployment defaults page sets, taken as RunInputs.Capture takes it; what is not set gives no value.
    public static IReadOnlyList<NamedValue> DeploymentDefaults(DeploymentOptions deployment)
    {
        ArgumentNullException.ThrowIfNull(deployment);

        (string Name, string? Value)[] settings =
        [
            (TimeZone, deployment.TimeZone),
            (Locale, deployment.Locale),
            (Keyboard, deployment.Keyboard),
            (OrganizationalUnit, deployment.Domain.OrganizationalUnit),
            (AdministratorName, deployment.LocalAdministrator.Name),
        ];

        return [.. settings.Where(setting => !string.IsNullOrWhiteSpace(setting.Value)).Select(setting => new NamedValue(setting.Name, setting.Value!.Trim()))];
    }

    // A run's answers by input name, ignoring case, as ValueSources takes them.
    public static IReadOnlyDictionary<string, string> Answers(IEnumerable<RunAnswer> answers)
    {
        ArgumentNullException.ThrowIfNull(answers);

        Dictionary<string, string> byName = new(StringComparer.OrdinalIgnoreCase);

        foreach (RunAnswer answer in answers)
        {
            byName[answer.Name] = answer.Value;
        }

        return byName;
    }

    // The problems that keep a run from starting, as a page shows them: a problem's Field is the value's or input's name.
    public static IReadOnlyList<SequenceProblem> ProblemsOf(ValueResolution resolution)
    {
        ArgumentNullException.ThrowIfNull(resolution);

        return [.. resolution.Problems.Select(problem => SequenceProblem.From(null, problem.Name, problem.Message))];
    }
}
