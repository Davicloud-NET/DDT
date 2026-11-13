// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Sequences;
using DDT.Core.Templates;
using DDT.Core.Unattend;
using DDT.Server.Rules;

namespace DDT.Server.Deployments;

// The answer file of a Write answer file step. It holds the settings taken when the run started, the step's own
// overrides, and the secrets from the settings, which the caller reads. Without a product key, an organization, an
// owner, a static network and with the default switches, it's the answer file DDT always wrote.
public sealed class UnattendRenderer
{
    // Only x64 images can be deployed, so every component is the amd64 one.
    private const string ProcessorArchitecture = "amd64";
    private const string FallbackLanguage = "en-US";

    // Setup generates a name for "*".
    private const string GeneratedComputerName = "*";

    // The answer file as the agent fetches it. Values are the run's start values, the variables its steps set since and
    // the machine's facts, which the step's templates such as {{TimeZone}} read. Problem is why it cannot be written.
    public (string? AnswerFile, string? Problem) Render(UnattendSources sources, WriteUnattendStep step, Func<string, string?> values)
    {
        (UnattendSettings? settings, string? problem) = Settings(sources, step, values);

        return settings is null ? (null, problem) : (UnattendWriter.Write(settings), null);
    }

    // The computer name and the settings the run started with are taken from the values too, so a step that changed one
    // counts. The result is checked like a sequence's settings, because a value can hold anything.
    public (UnattendSettings? Settings, string? Problem) Settings(UnattendSources sources, WriteUnattendStep step, Func<string, string?> values)
    {
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentNullException.ThrowIfNull(step);
        ArgumentNullException.ThrowIfNull(values);

        (WriteUnattendStep? worked, string? problem) = Worked(step, values);

        if (worked is null)
        {
            return (null, problem);
        }

        // The image's own language was never checked, so only a language the step names is.
        if (worked.UiLanguage is { } language && !WindowsSetupValues.IsUiLanguage(language))
        {
            return (null, $"The answer file's display language, {language}, is not a language tag such as de-DE.");
        }

        RunInputs inputs = sources.Inputs;
        RunInputs current = inputs with
        {
            ComputerName = Value(values(MachineVariableNames.ComputerName)) ?? inputs.ComputerName,
            TimeZone = Value(values(MachineValues.TimeZone)) ?? inputs.TimeZone,
            Locale = Value(values(MachineValues.Locale)) ?? inputs.Locale,
            Keyboard = Value(values(MachineValues.Keyboard)) ?? inputs.Keyboard,
            Organization = Value(values(MachineValues.Organization)) ?? inputs.Organization,
            Owner = Value(values(MachineValues.Owner)) ?? inputs.Owner,
        };
        UnattendSettings plain = Settings(sources with { Inputs = current }, worked);
        (UnattendSettings? settings, problem) = WithValues(plain, sources, values);

        return settings is null ? (null, problem)
            : Problem(settings) is { } refused ? (null, refused)
            : (settings, null);
    }

    // The settings without values: the run's start values, the step and the secrets.
    public UnattendSettings Settings(UnattendSources sources, WriteUnattendStep step)
    {
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentNullException.ThrowIfNull(step);

        RunInputs inputs = sources.Inputs;
        string uiLanguage = Value(step.UiLanguage) ?? Value(sources.ImageLanguage) ?? FallbackLanguage;
        string locale = Value(step.Locale) ?? Value(inputs.Locale) ?? uiLanguage;
        return new UnattendSettings(
            ProcessorArchitecture,
            Value(inputs.ComputerName) ?? GeneratedComputerName,
            Value(step.TimeZone) ?? Value(inputs.TimeZone),
            uiLanguage,
            locale,
            Value(step.Keyboard) ?? Value(inputs.Keyboard) ?? locale,
            step.LocalAdministrator && !string.IsNullOrEmpty(sources.Password) ? new LocalAdministrator(inputs.AdministratorName, sources.Password) : null)
        {
            RegisteredOrganization = Value(step.Organization) ?? Value(inputs.Organization),
            RegisteredOwner = Value(step.Owner) ?? Value(inputs.Owner),
            Oobe = new OobePages(step.HideEulaPage, step.HideOemRegistration, step.HideOnlineAccounts, step.HideWirelessSetup),
            FirstLogonCommands = sources.FirstLogon?.Commands ?? [],
            AutoLogon = sources.FirstLogon?.AutoLogon,
        };
    }

    private static (UnattendSettings? Settings, string? Problem) WithValues(UnattendSettings settings, UnattendSources sources, Func<string, string?> values)
    {
        (ProductKey? key, string? keyProblem) = AnswerFileValues.ProductKeyOf(values, sources.ProductKey);

        if (keyProblem is not null)
        {
            return (null, keyProblem);
        }

        if (sources.MacAddress is not { } mac)
        {
            return (settings with { ProductKey = key }, null);
        }

        (StaticNetwork? network, string? networkProblem) = AnswerFileValues.NetworkOf(values, mac);

        return networkProblem is null ? (settings with { ProductKey = key, Network = network }, null) : (null, networkProblem);
    }

    // The step's settings worked out from the values, or the problem with the first one that can't be.
    private static (WriteUnattendStep? Step, string? Problem) Worked(WriteUnattendStep step, Func<string, string?> values)
    {
        string? problem = null;

        string? Work(string? written, string setting)
        {
            if (Value(written) is not { } text || problem is not null)
            {
                return null;
            }

            if (ValueTemplate.TryRender(text, values, out string rendered, out TemplateProblem? refused))
            {
                return Value(rendered);
            }

            problem = $"The answer file's {setting}, {text}, cannot be worked out from the run's values. {refused?.Message().Text}";

            return null;
        }

        WriteUnattendStep worked = step with
        {
            TimeZone = Work(step.TimeZone, "time zone"),
            Locale = Work(step.Locale, "locale"),
            Keyboard = Work(step.Keyboard, "keyboard"),
            UiLanguage = Work(step.UiLanguage, "display language"),
            Organization = Work(step.Organization, "organization"),
            Owner = Work(step.Owner, "owner"),
        };

        return problem is null ? (worked, null) : (null, problem);
    }

    private static string? Problem(UnattendSettings settings) => settings switch
    {
        { ComputerName: not GeneratedComputerName and var name } when ComputerNames.Problem(name) is { } refused =>
            $"The computer name {name} cannot be used in the answer file. {refused.Text}",
        { TimeZone: { } timeZone } when !WindowsSettings.IsTimeZone(timeZone) =>
            $"The answer file's time zone, {timeZone}, is not a Windows time zone.",
        { Locale: var locale } when !WindowsSettings.IsLocale(locale) =>
            $"The answer file's locale, {locale}, is not a locale that names a region.",
        { Keyboard: var keyboard } when !WindowsSettings.IsKeyboard(keyboard) =>
            $"The answer file's keyboard, {keyboard}, is not a list of keyboards Windows knows.",
        { RegisteredOrganization.Length: > WindowsSetupValues.MaxRegisteredLength } or { RegisteredOwner.Length: > WindowsSetupValues.MaxRegisteredLength } =>
            $"The answer file's organization and owner may have at most {WindowsSetupValues.MaxRegisteredLength} characters each.",
        _ => null,
    };

    private static string? Value(string? setting) => string.IsNullOrWhiteSpace(setting) ? null : setting.Trim();
}
