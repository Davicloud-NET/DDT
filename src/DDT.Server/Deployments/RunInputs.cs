// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Text.Json;
using DDT.Contracts.Sequences;
using DDT.Server.Machines;
using DDT.Server.Rules;

namespace DDT.Server.Deployments;

// The settings a run's answer file and domain join use, taken when the run starts, so that changing them later never
// changes a run halfway. Never a secret: the passwords are read from the configuration when the agent fetches them.
// They come from the run's values (From), where the deployment defaults are the last source, so without a rule, a
// machine role, an input or a variable that sets one they are the settings as before.
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

    public static RunInputs Read(string inputs) =>
        JsonSerializer.Deserialize(inputs, DeploymentJsonContext.Default.RunInputs)
        ?? throw new InvalidDataException("The inputs a run stored are null.");

    public string Write() => JsonSerializer.Serialize(this, DeploymentJsonContext.Default.RunInputs);

    private static string? Value(string? setting) => string.IsNullOrWhiteSpace(setting) ? null : setting.Trim();
}
