// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Text;
using System.Text.RegularExpressions;

namespace DDT.Core.Templates;

// Text with placeholders for values, such as PC-{{SerialNumber|alnum|right:12}}: a name, ignoring case, and filters
// after bars, done from left to right. upper and lower change the case, trim takes the white space off both ends, alnum
// keeps only the letters A to Z and the digits, and left:n and right:n keep the first or last n characters. Nothing is
// cut silently: a name's value goes in whole unless a filter says otherwise. Text between double braces that is not a
// name followed by filters, such as Jinja's {{ v1.local_hostname }}, is not a placeholder and stays as it is.
//
// The web mirrors this in its template field, checked against src/DDT.Web/src/test/fixtures/template-cases.json.
public static partial class ValueTemplate
{
    // The most characters left:n and right:n take.
    public const int MaxCount = 1024;

    public const string Upper = "upper";
    public const string Lower = "lower";
    public const string Trim = "trim";
    public const string Alnum = "alnum";
    public const string Left = "left";
    public const string Right = "right";

    public static IReadOnlyList<string> Filters { get; } = [Upper, Lower, Trim, Alnum, Left, Right];

    // The filters as a person writes them, for a message that lists them.
    internal static string FilterList => $"{Upper}, {Lower}, {Trim}, {Alnum}, {Left}:n, {Right}:n";

    // Known says which names the caller knows, ignoring case; null knows every name. Filter problems are always reported.
    public static ParsedTemplate Parse(string text, Func<string, bool>? known = null)
    {
        ArgumentNullException.ThrowIfNull(text);

        List<TemplatePlaceholder> placeholders = [];
        List<TemplateProblem> problems = [];

        foreach (Match match in Placeholder().Matches(text))
        {
            TemplatePlaceholder placeholder = Read(match);
            placeholders.Add(placeholder);

            if (known is not null && !known(placeholder.Name))
            {
                problems.Add(new TemplateProblem(TemplateProblemKind.UnknownName, placeholder.Text, placeholder.Name));
            }

            problems.AddRange(placeholder.Filters.Select(filter => FilterProblem(placeholder, filter)).OfType<TemplateProblem>());
        }

        return new ParsedTemplate(placeholders, [.. problems.Distinct()]);
    }

    // The text with each placeholder's value, filtered. Values gives a name's value, or null when there is none, which
    // is a problem, as a filter written wrong is; rendering stops at the first.
    public static bool TryRender(string text, Func<string, string?> values, out string rendered, out TemplateProblem? problem)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(values);

        TemplateProblem? first = null;

        rendered = Replace(text, placeholder =>
        {
            if (first is not null)
            {
                return null;
            }

            first = placeholder.Filters.Select(filter => FilterProblem(placeholder, filter)).OfType<TemplateProblem>().FirstOrDefault();

            if (first is not null)
            {
                return null;
            }

            if (values(placeholder.Name) is { } value)
            {
                return Apply(placeholder, value);
            }

            first = new TemplateProblem(TemplateProblemKind.MissingValue, placeholder.Text, placeholder.Name);

            return null;
        });
        problem = first;

        if (problem is not null)
        {
            rendered = "";
        }

        return problem is null;
    }

    // As TryRender; a problem throws TemplateException.
    public static string Render(string text, Func<string, string?> values) =>
        TryRender(text, values, out string rendered, out TemplateProblem? problem) ? rendered : throw new TemplateException(problem!);

    // Values by name, ignoring case whatever the dictionary's comparer.
    public static string Render(string text, IReadOnlyDictionary<string, string> values)
    {
        ArgumentNullException.ThrowIfNull(values);

        return Render(text, Lookup(values));
    }

    // A name's value from values, ignoring case whatever the dictionary's comparer.
    public static Func<string, string?> Lookup(IReadOnlyDictionary<string, string> values)
    {
        ArgumentNullException.ThrowIfNull(values);

        return name =>
        {
            if (values.TryGetValue(name, out string? value))
            {
                return value;
            }

            foreach ((string key, string found) in values)
            {
                if (string.Equals(key, name, StringComparison.OrdinalIgnoreCase))
                {
                    return found;
                }
            }

            return null;
        };
    }

    // The value with the placeholder's filters done, which must have no problems.
    public static string Apply(TemplatePlaceholder placeholder, string value)
    {
        ArgumentNullException.ThrowIfNull(placeholder);
        ArgumentNullException.ThrowIfNull(value);

        foreach (TemplateFilter filter in placeholder.Filters)
        {
            value = filter.Name.ToLowerInvariant() switch
            {
                Upper => value.ToUpperInvariant(),
                Lower => value.ToLowerInvariant(),
                Trim => value.Trim(),
                Alnum => string.Concat(value.Where(char.IsAsciiLetterOrDigit)),
                Left when filter.Count is { } count => value.Length > count ? value[..count] : value,
                Right when filter.Count is { } count => value.Length > count ? value[^count..] : value,
                _ => throw new ArgumentException($"{placeholder.Text} has a filter written wrong.", nameof(placeholder)),
            };
        }

        return value;
    }

    // What is wrong with one filter, or null.
    public static TemplateProblem? FilterProblem(TemplatePlaceholder placeholder, TemplateFilter filter)
    {
        ArgumentNullException.ThrowIfNull(placeholder);
        ArgumentNullException.ThrowIfNull(filter);

        string name = filter.Name.ToLowerInvariant();

        if (!Filters.Contains(name, StringComparer.Ordinal))
        {
            return new TemplateProblem(TemplateProblemKind.UnknownFilter, placeholder.Text, placeholder.Name, filter.Name);
        }

        if (name is Left or Right ? filter.Count is not null : filter.Argument is null)
        {
            return null;
        }

        return new TemplateProblem(
            name is Left or Right ? TemplateProblemKind.FilterNeedsCount : TemplateProblemKind.FilterTakesNoCount,
            placeholder.Text,
            placeholder.Name,
            name);
    }

    // Each placeholder becomes what replace gives it; null leaves the placeholder as it is written.
    internal static string Replace(string text, Func<TemplatePlaceholder, string?> replace)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(replace);

        StringBuilder rendered = new(text.Length);
        int copied = 0;

        foreach (Match match in Placeholder().Matches(text))
        {
            rendered.Append(text, copied, match.Index - copied);
            rendered.Append(replace(Read(match)) ?? match.Value);
            copied = match.Index + match.Length;
        }

        return rendered.Append(text, copied, text.Length - copied).ToString();
    }

    private static TemplatePlaceholder Read(Match match)
    {
        string[] filters = match.Groups[2].Value.Split('|');
        List<TemplateFilter> read = [];

        // The text before the first bar is the white space after the name.
        foreach (string filter in filters.Skip(1))
        {
            int colon = filter.IndexOf(':', StringComparison.Ordinal);

            read.Add(colon < 0
                ? new TemplateFilter(filter.Trim(), null)
                : new TemplateFilter(filter[..colon].Trim(), filter[(colon + 1)..].Trim()));
        }

        return new TemplatePlaceholder(match.Value, match.Groups[1].Value, read);
    }

    // A name, then filters, each after a bar and holding no brace or bar.
    [GeneratedRegex(@"\{\{\s*([A-Za-z][A-Za-z0-9_]*)(\s*(?:\|[^{}|]*)*)\}\}", RegexOptions.CultureInvariant)]
    private static partial Regex Placeholder();
}
