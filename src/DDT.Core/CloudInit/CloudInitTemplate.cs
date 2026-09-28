// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Globalization;
using System.Text;
using DDT.Contracts.Sequences;
using DDT.Core.Templates;

namespace DDT.Core.CloudInit;

// Fills placeholders such as {{ComputerName}} in cloud-init seed files, escaped for a double-quoted YAML string. Other
// double braces are left alone, even a known name with a filter DDT doesn't have. cloud-init's Jinja templates use
// them too.
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

    // Every name in double braces that looks like a placeholder, known or not. Each name comes once, in order.
    public static IReadOnlyList<string> Placeholders(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        return ValueTemplate.Parse(text).Names;
    }

    // The known name a placeholder stands for, or null.
    public static string? Known(string placeholder) =>
        Names.FirstOrDefault(name => string.Equals(name, placeholder, StringComparison.OrdinalIgnoreCase));

    // A placeholder is a name from Names or any key in values, ignoring case. A name from Names without a value throws
    // an InvalidOperationException that names it. Line ends become LF, because shell scripts in user-data need that.
    public static string Render(string text, IReadOnlyDictionary<string, string?> values)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(values);

        string rendered = ValueTemplate.Replace(text, placeholder =>
        {
            if (placeholder.Filters.Any(filter => ValueTemplate.FilterProblem(placeholder, filter) is not null))
            {
                return null;
            }

            if (Known(placeholder.Name) is not { } name)
            {
                return Value(values, placeholder.Name) is { } runs ? Escape(ValueTemplate.Apply(placeholder, runs)) : null;
            }

            return Value(values, name) is { } value
                ? Escape(ValueTemplate.Apply(placeholder, value))
                : throw new InvalidOperationException($"The machine has no value for {{{{{name}}}}}.");
        });

        return rendered.ReplaceLineEndings("\n");
    }

    private static string? Value(IReadOnlyDictionary<string, string?> values, string name)
    {
        if (values.TryGetValue(name, out string? value))
        {
            return value;
        }

        foreach ((string key, string? found) in values)
        {
            if (string.Equals(key, name, StringComparison.OrdinalIgnoreCase))
            {
                return found;
            }
        }

        return null;
    }

    // Escapes the backslash, the quote and control characters for a double-quoted YAML string. That includes the C1
    // controls. Firmware strings read as Latin-1 can contain them, and YAML parsers refuse them unescaped.
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
