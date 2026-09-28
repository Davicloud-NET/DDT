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

// The values a run on a machine works with, from the sources in the order ValueSources lets them win: the answers, the
// machine's own values, the matching rules from the top, their machine roles, the sequence's defaults and the deployment
// defaults. The rules are walked again each time, so a rule changed after an assignment counts when the run starts.
public sealed class MachineValues(SequenceResolver resolver)
{
    // The names the deployment defaults give their values, as RunInputs takes them from the Deployment defaults page.
    public const string TimeZone = "TimeZone";
    public const string Locale = "Locale";
    public const string Keyboard = "Keyboard";
    public const string OrganizationalUnit = "OrganizationalUnit";
    public const string AdministratorName = "AdministratorName";

    public static IReadOnlyList<string> DeploymentDefaultNames { get; } = [TimeZone, Locale, Keyboard, OrganizationalUnit, AdministratorName];

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

        return Sources(machine, resolution, sequence, answers, deployment);
    }

    // For a caller that walked the rules already.
    public static ValueSources Sources(
        Machine machine,
        SequenceResolution resolution,
        SequenceDefinition? sequence,
        IReadOnlyDictionary<string, string>? answers,
        DeploymentOptions deployment)
    {
        ArgumentNullException.ThrowIfNull(machine);
        ArgumentNullException.ThrowIfNull(resolution);
        ArgumentNullException.ThrowIfNull(deployment);

        return new ValueSources
        {
            Sequence = sequence,
            Answers = answers ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            Machine = Own(machine),
            Rules = resolution.Match.RuleValues,
            Roles = resolution.Match.RoleValues,
            DeploymentDefaults = DeploymentDefaults(deployment),
            Facts = resolution.Machine,
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
