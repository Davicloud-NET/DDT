// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.MachineConsole.Keyboard;
using Xunit;

namespace DDT.MachineConsole.Tests.Keyboard;

public sealed class KeyboardLayoutTests
{
    // Golden rows: alone, Shift, AltGr, Shift+AltGr, then the same with Caps Lock. A dead key shows as ! and its accent.
    [Theory]
    [InlineData("de", 21, "z Z ← ¥ Z z ← ¥")]
    [InlineData("de", 3, "2 \" ² ⅛ 2 \" ² ⅛")]
    [InlineData("de", 40, "ä Ä !^ !ˇ Ä ä !^ !ˇ")]
    [InlineData("us", 3, "2 @ 2 @ 2 @ 2 @")]
    [InlineData("us", 16, "q Q q Q Q q Q q")]
    [InlineData("fr", 16, "a A æ Æ A a Æ æ")]
    [InlineData("fr", 3, "é 2 !~ ⅛ É 2 !~ ⅛")]
    [InlineData("ch", 40, "ä à { !ˇ Ä À { !ˇ")]
    public void HasTheRowsXkbHas(string name, ushort code, string row)
    {
        KeyboardLayout layout = Keys.Layout(name);
        string[] cells =
        [
            .. from caps in new[] { false, true } from altGr in new[] { false, true } from shift in new[] { false, true }
            select Show(layout, layout.Cell(code, shift, altGr && layout.AltGr, caps)),
        ];

        Assert.Equal(row, string.Join(' ', cells));
    }

    [Fact]
    public void KnowsEveryLayoutTheBootImagePageAndTheTextConsoleOffer()
    {
        string root = Repository.Root();
        string page = File.ReadAllText(Path.Combine(root, "src", "DDT.Web", "src", "boot", "keyboardLayouts.ts"));
        IEnumerable<string> textConsole = File.ReadAllLines(Path.Combine(root, "build", "LinuxBoot", "keymaps.txt"))
            .Where(line => line.Length > 0 && !line.StartsWith('#'))
            .Select(line => line.Split(' ', 2)[0]);
        IEnumerable<string> offered = page.Split('"').Where(part => part.Length == 13 && part[4] == ':');

        Assert.All(offered.Concat(textConsole), name => Assert.NotNull(KeyboardLayouts.FileOf(name)));
        Assert.Equal("de", KeyboardLayouts.FileOf(" 0407:00000407 "));
        Assert.Equal("ch-fr", KeyboardLayouts.FileOf("CH(fr)"));
        Assert.Null(KeyboardLayouts.FileOf("klingon"));
        Assert.Null(KeyboardLayouts.FileOf(null));
    }

    // Every file the generator writes is a layout someone can name, and every one of them parses.
    [Fact]
    public void HasAFileForEveryLayoutAndNoOther()
    {
        string folder = Path.Combine(Repository.Console, "Keyboard", "Layouts");
        string[] files = [.. Directory.GetFiles(folder, "*.keymap").Select(Path.GetFileNameWithoutExtension).Order().OfType<string>()];

        Assert.Equal(KeyboardLayouts.All.Order(), files);
        Assert.All(files, file => Assert.NotEmpty(KeyboardLayouts.Find(file, Keys.Read)!.Codes));
    }

    [Fact]
    public void RefusesALineThatIsNoKey()
    {
        Assert.Throws<FormatException>(() => KeyboardLayout.Parse("broken", "altgr 1\n16 71 51\n"));
        Assert.Throws<FormatException>(() => KeyboardLayout.Parse("broken", "16 71 51 40 3a9 51 71 40 3a9\n"));
    }

    [Fact]
    public void ReadsCellsAsCodePointsAndDeadKeys()
    {
        KeyboardLayout layout = KeyboardLayout.Parse("test", "altgr 0\n16 71 - ^301 1f600 51 51 51 51\ndead 301 20 27\ndead 301 65 e9\n");

        int[] cells = [.. Enumerable.Range(0, 4).Select(i => layout.Cell(16, (i & 1) != 0, (i & 2) != 0, false))];

        Assert.Equal(['q', 0, -0x301, 0x1F600], cells);
        Assert.Equal(0, layout.Cell(17, false, false, false));
        DeadKeys dead = layout.DeadKeys;
        Assert.Equal(("'", "\u00e9", "'x"), (dead.Alone(0x301), dead.Then(0x301, 'e'), dead.Then(0x301, 'x')));
    }

    private static string Show(KeyboardLayout layout, int cell) => cell switch
    {
        0 => "-",
        < 0 => "!" + layout.DeadKeys.Alone(-cell),
        _ => char.ConvertFromUtf32(cell),
    };
}
