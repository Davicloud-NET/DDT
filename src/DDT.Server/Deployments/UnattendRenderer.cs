// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Sequences;
using DDT.Core.Templates;
using DDT.Core.Unattend;
using DDT.Server.Rules;

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

    // The answer file as the agent fetches it: the step's settings may be templates, such as {{TimeZone}}, worked out from
    // values, which are the run's start values with the variables its steps set since and the machine's facts; the
    // computer name and the settings the run started with are taken from them too, so a step that changed one counts.
    // What comes out is checked as a sequence's settings are, since a value can hold anything. Problem is why the answer
    // file cannot be written, in the words of a run's error.
    public (string? AnswerFile, string? Problem) Render(
        RunInputs inputs,
        WriteUnattendStep step,
        string? imageLanguage,
        string? password,
        Func<string, string?> values)
    {
        (UnattendSettings? settings, string? problem) = Settings(inputs, step, imageLanguage, password, values);

        return settings is null ? (null, problem) : (UnattendWriter.Write(settings), null);
    }

    public (UnattendSettings? Settings, string? Problem) Settings(
        RunInputs inputs,
        WriteUnattendStep step,
        string? imageLanguage,
        string? password,
        Func<string, string?> values)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        ArgumentNullException.ThrowIfNull(step);
        ArgumentNullException.ThrowIfNull(values);

        string? problem = null;

        string? Worked(string? written, string setting)
        {
            if (Value(written) is not { } text || problem is not null)
            {
                return null;
            }

            if (ValueTemplate.TryRender(text, values, out string rendered, out TemplateProblem? refused))
            {
                return Value(rendered);
            }

            problem = $"The answer file's {setting}, {text}, cannot be worked out from the run's values. {refused!.Message().Text}";

            return null;
        }

        WriteUnattendStep worked = step with
        {
            TimeZone = Worked(step.TimeZone, "time zone"),
            Locale = Worked(step.Locale, "locale"),
            Keyboard = Worked(step.Keyboard, "keyboard"),
        };
        RunInputs current = inputs with
        {
            ComputerName = Value(values(MachineVariableNames.ComputerName)) ?? inputs.ComputerName,
            TimeZone = Value(values(MachineValues.TimeZone)) ?? inputs.TimeZone,
            Locale = Value(values(MachineValues.Locale)) ?? inputs.Locale,
            Keyboard = Value(values(MachineValues.Keyboard)) ?? inputs.Keyboard,
        };

        if (problem is not null)
        {
            return (null, problem);
        }

        UnattendSettings settings = Settings(current, worked, imageLanguage, password);

        problem = settings switch
        {
            { ComputerName: not GeneratedComputerName and var name } when ComputerNames.Problem(name) is { } refused =>
                $"The computer name {name} cannot be used in the answer file. {refused.Text}",
            { TimeZone: { } timeZone } when !WindowsSettings.IsTimeZone(timeZone) =>
                $"The answer file's time zone, {timeZone}, is not a Windows time zone.",
            { Locale: var locale } when !WindowsSettings.IsLocale(locale) =>
                $"The answer file's locale, {locale}, is not a locale that names a region.",
            { Keyboard: var keyboard } when !WindowsSettings.IsKeyboard(keyboard) =>
                $"The answer file's keyboard, {keyboard}, is not a list of keyboards Windows knows.",
            _ => null,
        };

        return problem is null ? (settings, null) : (null, problem);
    }

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
