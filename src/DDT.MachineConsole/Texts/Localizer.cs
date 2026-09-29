// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Globalization;
using System.Text;

namespace DDT.MachineConsole.Texts;

// Translates the console's own texts. Text from the agent is shown unchanged. Messages are English literals passed to
// T or F, and CatalogTests reads them from the code.
public sealed class Localizer
{
    private readonly Dictionary<UiLanguage, PoCatalog> _catalogs;

    public Localizer(UiLanguage language, IReadOnlyDictionary<UiLanguage, PoCatalog> catalogs)
    {
        ArgumentNullException.ThrowIfNull(catalogs);

        _catalogs = new Dictionary<UiLanguage, PoCatalog>(catalogs);
        Language = language;
    }

    public UiLanguage Language { get; private set; }

    public event EventHandler? Changed;

    // Uses the catalogs embedded in the console.
    public static Localizer Embedded(UiLanguage language) =>
        new(language, new Dictionary<UiLanguage, PoCatalog>
        {
            [UiLanguage.English] = Catalogs.Read("en"),
            [UiLanguage.German] = Catalogs.Read("de"),
        });

    public void Switch(UiLanguage language)
    {
        if (language == Language)
        {
            return;
        }

        Language = language;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public string T(string message)
    {
        ArgumentNullException.ThrowIfNull(message);

        return _catalogs.TryGetValue(Language, out PoCatalog? catalog) && catalog.Find(message) is { } translated
            ? translated
            : message;
    }

    // The message with each {name} replaced by its value. A placeholder without a value stays as it is.
    public string F(string message, params ReadOnlySpan<(string Name, string Value)> values) => Fill(T(message), values);

    public static string Fill(string text, ReadOnlySpan<(string Name, string Value)> values)
    {
        ArgumentNullException.ThrowIfNull(text);

        StringBuilder result = new(text.Length + 32);
        int index = 0;

        while (index < text.Length)
        {
            int open = text.IndexOf('{', index);
            int close = open < 0 ? -1 : text.IndexOf('}', open + 1);

            if (open < 0 || close < 0)
            {
                result.Append(text, index, text.Length - index);
                break;
            }

            result.Append(text, index, open - index);
            string name = text[(open + 1)..close];
            string? value = null;

            foreach ((string Name, string Value) pair in values)
            {
                if (pair.Name == name)
                {
                    value = pair.Value;
                    break;
                }
            }

            result.Append(value ?? text[open..(close + 1)]);
            index = close + 1;
        }

        return result.ToString();
    }

    // A number with at most the given decimals, with the decimal separator of the language: 5.9 or 5,9.
    public string Number(double value, int decimals = 0)
    {
        string text = Math.Round(value, decimals, MidpointRounding.AwayFromZero)
            .ToString(decimals == 0 ? "0" : "0." + new string('#', decimals), CultureInfo.InvariantCulture);

        return Language == UiLanguage.German ? text.Replace('.', ',') : text;
    }

    public string Number(long value) => value.ToString(CultureInfo.InvariantCulture);
}
