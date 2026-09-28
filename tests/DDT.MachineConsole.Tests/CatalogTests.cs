// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Text;
using System.Text.RegularExpressions;
using DDT.MachineConsole.Texts;
using Xunit;

namespace DDT.MachineConsole.Tests;

// Every message the code passes to T or F is in both catalogs with its placeholders: a missing one would show English
// on a German screen, and a dropped placeholder a hole in a sentence.
public sealed partial class CatalogTests
{
    // Files that pass T or F a message they got, rather than a literal.
    private static readonly string[] s_forwarders = ["Texts/Localizer.cs", "ViewModels/ScreenViewModel.cs"];

    [Fact]
    public void HasEveryMessageTheCodeSaysInEnglish()
    {
        Dictionary<string, string> used = UsedMessages();
        PoCatalog english = Catalog("en");

        Assert.Contains("Log", used.Keys);
        Assert.Empty(used.Keys.Where(message => !english.Messages.ContainsKey(message)).Order(StringComparer.Ordinal));
        Assert.Empty(english.Messages.Where(pair => pair.Key != pair.Value).Select(pair => pair.Key));

        // And nothing the code no longer says.
        Assert.Empty(english.Messages.Keys.Where(message => !used.ContainsKey(message)).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void TranslatesEveryMessageIntoGermanWithItsPlaceholders()
    {
        PoCatalog english = Catalog("en");
        PoCatalog german = Catalog("de");

        List<string> problems = [];

        foreach (string message in english.Messages.Keys)
        {
            if (german.Find(message) is not { } translated)
            {
                problems.Add($"no German for: {message}");
            }
            else if (!Placeholders(message).SetEquals(Placeholders(translated)))
            {
                problems.Add($"the German drops or adds a placeholder: {message} -> {translated}");
            }
        }

        Assert.Empty(problems);
        Assert.Equal(english.Messages.Count, german.Messages.Count);
    }

    [Fact]
    public void NamesEveryPlaceholder()
    {
        Assert.DoesNotContain(Catalog("en").Messages.Keys, message => NumberedPlaceholder().IsMatch(message));
    }

    [Fact]
    public void PassesOnlyLiteralsToTheLocalizer()
    {
        List<string> calls = [];

        foreach (string file in SourceFiles().Where(file => !s_forwarders.Contains(Relative(file))))
        {
            foreach (Match match in NonLiteralCall().Matches(StripComments(File.ReadAllText(file))))
            {
                calls.Add($"{Relative(file)}: {match.Value}");
            }
        }

        Assert.Empty(calls);
    }

    [Fact]
    public void ReadsTheCatalogsTheConsoleCarries()
    {
        Localizer german = Localizer.Embedded(UiLanguage.German);

        Assert.Equal("Protokoll", german.T("Log"));
        Assert.Equal("Datenträger 2 löschen?", german.F("Erase disk {number}?", ("number", "2")));
    }

    // Every literal message passed to T or F in the console's code, with the file it is in.
    private static Dictionary<string, string> UsedMessages()
    {
        Dictionary<string, string> used = new(StringComparer.Ordinal);

        foreach (string file in SourceFiles())
        {
            foreach (Match match in Call().Matches(StripComments(File.ReadAllText(file))))
            {
                string message = string.Concat(Literal().Matches(match.Groups[1].Value).Select(literal => Unescape(literal.Value)));
                used.TryAdd(message, Relative(file));
            }
        }

        return used;
    }

    private static IEnumerable<string> SourceFiles() =>
        Directory.EnumerateFiles(Repository.Console, "*.cs", SearchOption.AllDirectories)
            .Where(file => !Relative(file).StartsWith("obj/", StringComparison.Ordinal) && !Relative(file).StartsWith("bin/", StringComparison.Ordinal));

    private static string Relative(string file) => Path.GetRelativePath(Repository.Console, file).Replace('\\', '/');

    private static PoCatalog Catalog(string code)
    {
        using StreamReader reader = new(Path.Combine(Repository.Console, "Locales", code, "messages.po"), Encoding.UTF8);

        return PoCatalog.Read(reader);
    }

    private static HashSet<string> Placeholders(string text) => [.. Placeholder().Matches(text).Select(match => match.Value)];

    private static string StripComments(string code) => LineComment().Replace(code, string.Empty);

    private static string Unescape(string literal)
    {
        StringBuilder text = new();

        for (int index = 1; index < literal.Length - 1; index++)
        {
            if (literal[index] == '\\')
            {
                index++;
                text.Append(literal[index] switch { 'n' => '\n', 't' => '\t', _ => literal[index] });
            }
            else
            {
                text.Append(literal[index]);
            }
        }

        return text.ToString();
    }

    [GeneratedRegex("""\b[TF]\(\s*("(?:[^"\\]|\\.)*"(?:\s*\+\s*"(?:[^"\\]|\\.)*")*)""")]
    private static partial Regex Call();

    // A call of T or F whose message is not a literal; the declarations, "string T(", are not calls.
    [GeneratedRegex("""(?<!string )\b[TF]\((?!\s*")[^)\n]*""")]
    private static partial Regex NonLiteralCall();

    [GeneratedRegex(@"""(?:[^""\\]|\\.)*""")]
    private static partial Regex Literal();

    [GeneratedRegex(@"\{[A-Za-z]\w*\}")]
    private static partial Regex Placeholder();

    [GeneratedRegex(@"\{\d+\}")]
    private static partial Regex NumberedPlaceholder();

    [GeneratedRegex(@"^\s*//.*$", RegexOptions.Multiline)]
    private static partial Regex LineComment();
}
