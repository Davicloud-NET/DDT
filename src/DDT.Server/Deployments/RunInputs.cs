// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Text.Json;
using DDT.Contracts.Sequences;
using DDT.Core.Sequences;
using DDT.Server.Machines;
using DDT.Server.Rules;

namespace DDT.Server.Deployments;

// The settings a run's answer file and join use, taken from its values when it starts, so later changes never change a
// run halfway; never a secret. Facts are the machine's as they were then; null where a run started before they were kept.
public sealed record RunInputs(
    string? ComputerName,
    string? TimeZone,
    string? Locale,
    string? Keyboard,
    string AdministratorName,
    string? DomainName,
    string? DomainOrganizationalUnit,
    DateTimeOffset CapturedUtc)
{
    public IReadOnlyDictionary<string, string>? Facts { get; init; }

    public static RunInputs Capture(Machine machine, DeploymentOptions options, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(machine);
        ArgumentNullException.ThrowIfNull(options);

        return new RunInputs(
            Value(machine.AssignedName),
            Value(options.TimeZone),
            Value(options.Locale),
            Value(options.Keyboard),
            options.LocalAdministrator.Name.Trim(),
            Value(options.Domain.Name),
            Value(options.Domain.OrganizationalUnit),
            now);
    }

    // Taken from the values the run started with, by the names MachineValues gives the deployment defaults. The domain
    // is always the configured one: a value naming another could send the join account to a foreign domain controller.
    public static RunInputs From(IReadOnlyDictionary<string, string> values, DeploymentOptions options, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(values);
        ArgumentNullException.ThrowIfNull(options);

        string? Named(string name) => Value(values.GetValueOrDefault(name));

        return new RunInputs(
            Named(MachineVariableNames.ComputerName),
            Named(MachineValues.TimeZone),
            Named(MachineValues.Locale),
            Named(MachineValues.Keyboard),
            Named(MachineValues.AdministratorName) ?? options.LocalAdministrator.Name.Trim(),
            Value(options.Domain.Name),
            Named(MachineValues.OrganizationalUnit),
            now);
    }

    // The machine's facts by name as they are now, each as a template puts it in: the ones that stay the same while a run
    // goes on, so not Phase, which the hand-over to Windows changes, nor ComputerName, which is one of the run's values.
    public static IReadOnlyDictionary<string, string> FactsOf(Machine machine)
    {
        ArgumentNullException.ThrowIfNull(machine);

        MachineVariables variables = MachineVariableReader.Read(machine);
        Dictionary<string, string> facts = new(StringComparer.Ordinal);

        foreach (string name in MachineVariableNames.Catalogue.Keys)
        {
            if (name != MachineVariableNames.ComputerName
                && !MachineVariableNames.ChangeDuringRun.Contains(name, StringComparer.Ordinal)
                && variables.Value(name) is { } value)
            {
                facts[name] = value;
            }
        }

        return facts;
    }

    public static RunInputs Read(string inputs) =>
        JsonSerializer.Deserialize(inputs, DeploymentJsonContext.Default.RunInputs)
        ?? throw new InvalidDataException("The inputs a run stored are null.");

    public string Write() => JsonSerializer.Serialize(this, DeploymentJsonContext.Default.RunInputs);

    private static string? Value(string? setting) => string.IsNullOrWhiteSpace(setting) ? null : setting.Trim();
}
