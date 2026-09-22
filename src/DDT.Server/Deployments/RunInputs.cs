// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Text.Json;
using DDT.Server.Machines;

namespace DDT.Server.Deployments;

// The settings a run's answer file and domain join use, taken when the run starts, so that changing them later never
// changes a run halfway. Never a secret: the passwords are read from the configuration when the agent fetches them.
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

    public static RunInputs Read(string inputs) =>
        JsonSerializer.Deserialize(inputs, DeploymentJsonContext.Default.RunInputs)
        ?? throw new InvalidDataException("The inputs a run stored are null.");

    public string Write() => JsonSerializer.Serialize(this, DeploymentJsonContext.Default.RunInputs);

    private static string? Value(string? setting) => string.IsNullOrWhiteSpace(setting) ? null : setting.Trim();
}
