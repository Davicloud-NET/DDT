// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Globalization;
using System.Runtime.Versioning;
using System.Text.RegularExpressions;
using DDT.MachineConsole.Keyboard;
using Xunit;

namespace DDT.MachineConsole.Tests.Keyboard;

// The evidence that a password typed in Windows PE types the same in Linux: every printing key of each layout the boot
// image page offers, and every dead key, against the layout Windows ships. Where Windows types something and Linux
// types something else, the line must be in WindowsDifferences.txt. Keys Windows leaves empty don't count.
[SupportedOSPlatform("windows")]
public sealed partial class WindowsLayoutTests
{
    private static readonly ushort[] s_printingKeys =
        [.. new[] { (2, 12), (16, 12), (30, 12), (43, 11), (57, 1), (86, 1) }
            .SelectMany(range => Enumerable.Range(range.Item1, range.Item2)).Select(code => (ushort)code)];

    [Fact]
    public void TypesWhatWindowsTypesButForTheListedDifferences()
    {
        string expectedPath = Path.Combine(Repository.Root(), "tests", "DDT.MachineConsole.Tests", "Keyboard", "WindowsDifferences.txt");
        List<string> differences = [];

        foreach (string id in OfferedIds())
        {
            string file = KeyboardLayouts.FileOf(id) ?? throw new InvalidOperationException($"No layout for {id}.");
            differences.AddRange(Compare(id, WindowsLayout.Load(id), KeyboardLayout.Parse(file, Read($"{file}.keymap"))));
        }

        string found = string.Join('\n', differences) + "\n";

        if (Environment.GetEnvironmentVariable("DDT_WRITE_KEYBOARD_DIFFERENCES") == "1")
        {
            File.WriteAllText(expectedPath, found);
        }

        Assert.Equal(File.ReadAllText(expectedPath).ReplaceLineEndings("\n"), found);
    }

    private static IEnumerable<string> Compare(string id, WindowsLayout windows, KeyboardLayout linux)
    {
        string name = $"{id} {linux.Name}";

        if (windows.HasAltGr != linux.AltGr)
        {
            yield return $"{name}: right Alt is AltGr on {(windows.HasAltGr ? "Windows" : "Linux")} only";
        }

        HashSet<char> typed = [' '];
        List<(char Accent, int Mark)> deads = [];

        foreach (ushort code in s_printingKeys)
        {
            foreach ((bool shift, bool altGr, bool caps) in Combinations())
            {
                (char ours, bool oursDead) = Ours(linux.Cell(code, shift, altGr && linux.AltGr, caps), linux);
                (char theirs, bool theirsDead) = windows.Cell(code, shift, altGr && windows.HasAltGr, caps);

                if (theirs != 0 && !theirsDead)
                {
                    typed.Add(theirs);
                }

                if (theirsDead && oursDead && ours == theirs)
                {
                    deads.Add((theirs, -linux.Cell(code, shift, altGr && linux.AltGr, caps)));
                }

                if (theirs != 0 && (ours != theirs || oursDead != theirsDead))
                {
                    yield return $"{name} key {code} {Name(shift, altGr, caps)}: " +
                        $"Windows {Show(theirs, theirsDead)}, Linux {Show(ours, oursDead)}";
                }
            }
        }

        foreach ((char accent, int mark) in deads.Distinct())
        {
            foreach (char next in typed.Order())
            {
                string theirs = windows.DeadKeys.TryGetValue((accent, next), out char made) ? made.ToString() : $"{accent}{next}";
                string ours = linux.DeadKeys.Then(mark, next);

                if (ours != theirs)
                {
                    yield return $"{name} {accent} then {Show(next, false)}: Windows {theirs}, Linux {ours}";
                }
            }
        }
    }

    private static (char Char, bool Dead) Ours(int cell, KeyboardLayout linux) => cell switch
    {
        < 0 => (linux.DeadKeys.Alone(-cell)[0], true),
        0 or > 0xFFFF => default,
        _ => ((char)cell, false),
    };

    private static IEnumerable<(bool Shift, bool AltGr, bool Caps)> Combinations() =>
        from caps in new[] { false, true }
        from altGr in new[] { false, true }
        from shift in new[] { false, true }
        select (shift, altGr, caps);

    private static string Name(bool shift, bool altGr, bool caps) =>
        string.Join('+', new[] { caps ? "Caps" : null, shift ? "Shift" : null, altGr ? "AltGr" : null }
            .OfType<string>()
            .DefaultIfEmpty("alone"));

    private static string Show(char value, bool dead) => value switch
    {
        (char)0 => "nothing",
        ' ' => "space",
        _ => dead ? $"dead {value}" : $"{value} (U+{(int)value:X4})",
    };

    private static IEnumerable<string> OfferedIds()
    {
        string offered = File.ReadAllText(Path.Combine(Repository.Root(), "src", "DDT.Web", "src", "boot", "keyboardLayouts.ts"));

        return OfferedId().Matches(offered)
            .Select(match => match.Groups[1].Value.ToLower(CultureInfo.InvariantCulture))
            .Distinct(StringComparer.Ordinal);
    }

    private static string Read(string name) => File.ReadAllText(Path.Combine(Repository.Console, "Keyboard", "Layouts", name));

    [GeneratedRegex("id: \"[0-9a-f]{4}:([0-9a-f]{8})\"", RegexOptions.IgnoreCase)]
    private static partial Regex OfferedId();
}
