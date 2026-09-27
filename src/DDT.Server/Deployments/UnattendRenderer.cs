// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Sequences;
using DDT.Core.Unattend;

namespace DDT.Server.Deployments;

// The answer file of a Write answer file step: the settings taken when the run started, the step's own overrides, and
// the local administrator's password as the settings hold it now, which the caller reads.
public sealed class UnattendRenderer
{
    // Only x64 images can be deployed, so every component is the amd64 one.
    private const string ProcessorArchitecture = "amd64";
    private const string FallbackLanguage = "en-US";

    // Setup generates a name for "*".
    private const string GeneratedComputerName = "*";

    public string Render(RunInputs inputs, WriteUnattendStep step, string? imageLanguage, string? password) =>
        UnattendWriter.Write(Settings(inputs, step, imageLanguage, password));

    public UnattendSettings Settings(RunInputs inputs, WriteUnattendStep step, string? imageLanguage, string? password)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        ArgumentNullException.ThrowIfNull(step);

        string uiLanguage = Value(imageLanguage) ?? FallbackLanguage;
        string locale = Value(step.Locale) ?? Value(inputs.Locale) ?? uiLanguage;

        return new UnattendSettings(
            ProcessorArchitecture,
            Value(inputs.ComputerName) ?? GeneratedComputerName,
            Value(step.TimeZone) ?? Value(inputs.TimeZone),
            uiLanguage,
            locale,
            Value(step.Keyboard) ?? Value(inputs.Keyboard) ?? locale,
            step.LocalAdministrator && !string.IsNullOrEmpty(password) ? new LocalAdministrator(inputs.AdministratorName, password) : null);
    }

    private static string? Value(string? setting) => string.IsNullOrWhiteSpace(setting) ? null : setting.Trim();
}
