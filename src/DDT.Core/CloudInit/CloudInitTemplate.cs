// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Globalization;
using System.Text;
using DDT.Contracts.Sequences;
using DDT.Core.Templates;

namespace DDT.Core.CloudInit;

// The seed files of a Write the cloud-init seed step are text with placeholders such as {{ComputerName}} for the
// machine's values, written as ValueTemplate writes them, filters included. A value is escaped for a double-quoted YAML
// string, where the placeholders belong. Only these names are placeholders, ignoring case; anything else between double
// braces stays as it is, because cloud-init's own Jinja templates use the same braces, and so does a known name with a
// filter DDT does not have.
public static class CloudInitTemplate
{
    public static IReadOnlyList<string> Names { get; } =
    [
        MachineVariableNames.ComputerName,
        MachineVariableNames.Manufacturer,
        MachineVariableNames.Model,
        MachineVariableNames.SerialNumber,
        MachineVariableNames.SmbiosUuid,
        MachineVariableNames.MacAddress,
    ];

    // Every name between double braces that looks like a placeholder, known or not, once each, in order.
    public static IReadOnlyList<string> Placeholders(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        return ValueTemplate.Parse(text).Names;
    }

    // The known name a placeholder stands for, or null.
    public static string? Known(string placeholder) =>
        Names.FirstOrDefault(name => string.Equals(name, placeholder, StringComparison.OrdinalIgnoreCase));

    // Replaces each known placeholder with its value from values, keyed by the names in Names. Throws
    // InvalidOperationException, whose message names the placeholder, when a value the text uses is missing. Line ends
    // become LF, which shell scripts in user-data need.
    public static string Render(string text, IReadOnlyDictionary<string, string?> values)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(values);

        string rendered = ValueTemplate.Replace(text, placeholder =>
        {
            if (Known(placeholder.Name) is not { } name
                || placeholder.Filters.Any(filter => ValueTemplate.FilterProblem(placeholder, filter) is not null))
            {
                return null;
            }

            return values.TryGetValue(name, out string? value) && value is not null
                ? Escape(ValueTemplate.Apply(placeholder, value))
                : throw new InvalidOperationException($"The machine has no value for {{{{{name}}}}}.");
        });

        return rendered.ReplaceLineEndings("\n");
    }

    // What YAML needs escaped in a double-quoted string: the backslash, the quote and control characters, the C1 ones
    // too, which firmware strings read as Latin-1 can hold and YAML parsers refuse unescaped.
    private static string Escape(string value)
    {
        StringBuilder escaped = new(value.Length);

        foreach (char character in value)
        {
            switch (character)
            {
                case '\\':
                    escaped.Append(@"\\");
                    break;
                case '"':
                    escaped.Append("\\\"");
                    break;
                case < ' ' or (>= '\u007F' and <= '\u009F'):
                    escaped.Append(CultureInfo.InvariantCulture, $"\\x{(int)character:x2}");
                    break;
                default:
                    escaped.Append(character);
                    break;
            }
        }

        return escaped.ToString();
    }
}
