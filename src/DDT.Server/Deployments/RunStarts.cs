// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Deployments;
using DDT.Contracts.Machines;
using DDT.Contracts.Messages;
using DDT.Contracts.Sequences;
using DDT.Core.Sequences;
using DDT.Server.Data;
using DDT.Server.Machines;
using DDT.Server.Rules;
using DDT.Server.Settings;

namespace DDT.Server.Deployments;

// Starts an assigned run when its agent's first report or the answers given at the machine ask for it. Nothing here
// saves.
public sealed class RunStarts(DdtDbContext database, RunQueries queries, RunValues values, DdtSettings settings)
{
    // The values are worked out now, with one settings snapshot for the checks and the capture. So a save in between
    // can't start a run with values nobody checked. Anything that keeps the run from starting ends it before any disk
    // is touched. The exception is a required input the machine asks: the run waits at its start until someone answers
    // it.
    public async Task<RunStart> StartAsync(Machine machine, Deployment run, string? address, DateTimeOffset now, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(machine);
        ArgumentNullException.ThrowIfNull(run);

        SettingsSnapshot snapshot = settings.Current;
        SequenceDefinition definition = await queries.DefinitionAsync(run, cancellationToken).ConfigureAwait(false);
        string? problem = StartProblem(snapshot, definition);

        if (problem is null)
        {
            RunValueCheck check = await values.CheckAsync(machine, run, definition, snapshot.Deployment, cancellationToken).ConfigureAwait(false);
            InputDeclaration[] webOnly = [.. check.Missing.Where(input => input.AskAt == InputAsk.Web)];

            if (webOnly.Length == 0 && check.Missing.Count > 0)
            {
                run.InputsPending = true;

                return new RunStart(null, check.AskedAtMachine);
            }

            RunInputs inputs = RunInputs.From(check.Resolution.Effective, snapshot.Deployment, now) with { Facts = RunInputs.FactsOf(machine) };
            problem = ValuesProblem(check, webOnly) ?? SettingsProblem(definition, inputs);

            if (problem is null)
            {
                Start(machine, run, check, inputs, now);
                database.AuditEvents.Add(AuditEvents.Create(AuditActions.DeploymentStarted, run.Id.ToString("D"), Actor.OfMachine(machine.Id, address), now, $"{run.Title} on machine {machine.Id:D}."));

                return new RunStart(null, null);
            }
        }

        string error = StoredText.Bound(problem, DeploymentLimits.MaxErrorLength) ?? problem;
        run.InputsPending = false;
        RunTermination.End(machine, run, DeploymentState.Failed, error, now);
        machine.State = MachineState.Failed;
        database.AuditEvents.Add(AuditEvents.Create(AuditActions.DeploymentFailed, run.Id.ToString("D"), Actor.OfMachine(machine.Id, address), now, $"{run.Title} on machine {machine.Id:D} did not start: {error}"));

        return new RunStart(error, null);
    }

    private static void Start(Machine machine, Deployment run, RunValueCheck check, RunInputs inputs, DateTimeOffset now)
    {
        run.InputsPending = false;
        run.State = DeploymentState.Running;
        run.StartedUtc = now;
        run.UpdatedUtc = now;
        run.Values = RunValues.Write(check.Resolution.Values);
        run.Inputs = inputs.Write();
        machine.State = MachineState.Deploying;
    }

    private static string? ValuesProblem(RunValueCheck check, InputDeclaration[] webOnly) =>
        webOnly.Length > 0
            ? $"The run did not start, because only the web asks what it lacks: {Sentences(webOnly.Select(input => ServerMessages.ValuesInputRequired.With("label", input.Label).Text))} Assign the sequence again and answer it."
            : check.Problems.Count > 0
                ? $"The run's values have problems, so it did not start: {Sentences(check.Problems.Select(value => value.Message.Text))}"
                : null;

    // The settings a run needs can disappear between its assignment and its start, with a server restart. The run then
    // fails at once, instead of halfway through when the agent asks for them.
    private static string? StartProblem(SettingsSnapshot snapshot, SequenceDefinition definition)
    {
        // A run's error is in the agent's language, English, like every other run error.
        if (DeploymentPolicy.SettingsProblem(snapshot) is { } closed)
        {
            return closed.Text;
        }

        DeploymentOptions deployment = snapshot.Deployment;
        IReadOnlyList<SequenceStep> nodes = SequenceTree.Nodes(definition);

        if (nodes.OfType<WriteUnattendStep>().Any(s => s.LocalAdministrator) && string.IsNullOrEmpty(deployment.LocalAdministrator.Password))
        {
            return "The sequence adds the local administrator, but DDT:Deployment:LocalAdministrator has no password any more. Configure one and assign the sequence again.";
        }

        // A join that names an account joins that account's domain, and doesn't need the configured one.
        return nodes.OfType<JoinDomainStep>().Any(join => join.Account is null)
            && (string.IsNullOrWhiteSpace(deployment.Domain.Name)
                || string.IsNullOrWhiteSpace(deployment.Domain.UserName)
                || string.IsNullOrEmpty(deployment.Domain.Password))
                ? "The sequence joins the domain, but DDT:Deployment:Domain no longer names a domain and an account to join it with. Configure them and assign the sequence again."
                : null;
    }

    // A rule or an input can give a time zone, a locale, a keyboard or an organizational unit the settings page would
    // refuse. Only what the sequence uses is checked. The computer name was already checked with the values.
    private static string? SettingsProblem(SequenceDefinition definition, RunInputs inputs)
    {
        IReadOnlyList<SequenceStep> nodes = SequenceTree.Nodes(definition);

        if (nodes.Any(node => node is WriteUnattendStep))
        {
            if (inputs.TimeZone is { } timeZone && !WindowsSettings.IsTimeZone(timeZone))
            {
                return $"The run's {MachineValues.TimeZone} value, {timeZone}, is not a Windows time zone, so it did not start.";
            }

            if (inputs.Locale is { } locale && !WindowsSettings.IsLocale(locale))
            {
                return $"The run's {MachineValues.Locale} value, {locale}, is not a locale that names a region, so it did not start.";
            }

            if (inputs.Keyboard is { } keyboard && !WindowsSettings.IsKeyboard(keyboard))
            {
                return $"The run's {MachineValues.Keyboard} value, {keyboard}, is not a list of keyboards Windows knows, so it did not start.";
            }
        }

        return nodes.Any(node => node is JoinDomainStep)
            && inputs.DomainOrganizationalUnit is { } organizationalUnit
            && DeploymentOptionsValidation.OrganizationalUnitMessage(organizationalUnit) is { } refused
                ? $"The run's {MachineValues.OrganizationalUnit} value, {organizationalUnit}, cannot be used, so it did not start. {refused.Text}"
                : null;
    }

    private static string Sentences(IEnumerable<string> sentences) => string.Join(" ", sentences);
}
