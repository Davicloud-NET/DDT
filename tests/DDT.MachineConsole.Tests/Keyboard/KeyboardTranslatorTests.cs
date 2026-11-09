// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using Avalonia.Input;
using DDT.MachineConsole.Keyboard;
using Xunit;

namespace DDT.MachineConsole.Tests.Keyboard;

public sealed class KeyboardTranslatorTests
{
    // Spike H's password: every level of the German layout.
    [Fact]
    public void TypesAGermanPasswordWithAltGrExactly()
    {
        const string password = "Pa$$w0rd@€{[]}\\~|µ²³§°ß?ÄÖÜäöü";

        Assert.Equal(password, new Keys().Type(password).Typed);
    }

    [Fact]
    public void GivesAltGrNoModifierAndTheConsoleItsExactModifiers()
    {
        Keys keys = new Keys().Tap(16, Keys.AltGr).Tap(Keys.F10, Keys.Shift);

        KeyStroke at = Assert.Single(keys.Strokes, stroke => stroke is { Down: true, Key: Key.Q });
        Assert.Equal((KeyModifiers.None, "@"), (at.Modifiers, at.Text));
        KeyStroke prompt = Assert.Single(keys.Strokes, stroke => stroke is { Down: true, Key: Key.F10 });
        Assert.Equal((KeyModifiers.Shift, (string?)null), (prompt.Modifiers, prompt.Text));
    }

    [Fact]
    public void TypesNothingWithCtrlOrAltButNamesTheLetterOfTheLayout()
    {
        Keys keys = new Keys().Tap(21, Keys.Ctrl).Tap(46, EvdevKeys.LeftAlt);

        Assert.Equal("", keys.Typed);
        Assert.Equal(KeyModifiers.Control, Assert.Single(keys.Strokes, stroke => stroke is { Down: true, Key: Key.Z }).Modifiers);
        Assert.Equal(KeyModifiers.Alt, Assert.Single(keys.Strokes, stroke => stroke is { Down: true, Key: Key.C }).Modifiers);
    }

    // As on Windows: the letter, the accent before a space, and the accent and the key where they make nothing.
    [Theory]
    [InlineData(new ushort[] { 41, Keys.Space }, "^")]
    [InlineData(new ushort[] { 13, 18 }, "é")]
    [InlineData(new ushort[] { 41, 22 }, "û")]
    [InlineData(new ushort[] { 41, 2 }, "^1")]
    [InlineData(new ushort[] { 41, 41 }, "^")]
    [InlineData(new ushort[] { 13, 41 }, "´^")]
    public void ComposesDeadKeys(ushort[] codes, string typed)
    {
        Keys keys = new();

        foreach (ushort code in codes)
        {
            keys.Tap(code);
        }

        Assert.Equal(typed, keys.Typed);
    }

    [Fact]
    public void TypesAGraveAccentDeadKeyWithShift()
    {
        Assert.Equal("à", new Keys().Tap(13, Keys.Shift).Tap(30).Typed);
    }

    [Fact]
    public void LetsGoOfADeadKeyBeforeAKeyThatTypesNothing()
    {
        Keys keys = new Keys().Tap(41);
        keys.Strokes.Clear();
        keys.Tap(Keys.Enter);

        Assert.Equal(
            [(Key.None, "^"), (Key.Enter, null)],
            keys.Strokes.Where(stroke => stroke.Down).Select(stroke => (stroke.Key, stroke.Text)));
    }

    [Fact]
    public void FollowsCapsLock()
    {
        Keys keys = new Keys().Tap(EvdevKeys.CapsLock).Type("a").Tap(30, Keys.Shift).Tap(2);

        Assert.True(keys.Translator.CapsLock);
        Assert.Equal("Aa1", keys.Typed);
        Assert.False(keys.Tap(EvdevKeys.CapsLock).Translator.CapsLock);
    }

    [Fact]
    public void MovesWithTheKeypadWithoutNumLockOrWithShift()
    {
        Keys keys = new Keys().Tap(71);
        Assert.Equal([Key.Home, Key.Home], keys.Strokes.Select(stroke => stroke.Key));
        Assert.Equal("", keys.Typed);

        keys.Strokes.Clear();
        keys.Tap(EvdevKeys.NumLock).Tap(71).Tap(83).Tap(71, Keys.Shift);

        Assert.True(keys.Translator.NumLock);
        Assert.Equal("7,", keys.Typed);
        KeyStroke moved = keys.Strokes.Last(stroke => stroke.Down);
        Assert.Equal((Key.Home, KeyModifiers.None), (moved.Key, moved.Modifiers));
        Assert.Equal(Key.NumPad7, keys.Strokes.First(stroke => stroke.PhysicalKey == PhysicalKey.NumPad7).Key);
    }

    [Fact]
    public void RepeatsAHeldKeyButNotADeadKey()
    {
        Keys keys = new Keys().Down(30).Repeat(30).Repeat(30).Up(30).Down(41).Repeat(41).Up(41).Tap(18);

        Assert.Equal("aaaê", keys.Typed);
    }

    [Fact]
    public void HoldsModifiersOverEveryKeyboard()
    {
        Keys keys = new Keys()
            .Down(Keys.Shift, device: 1).Down(30, device: 2).Up(30, device: 2)
            .Up(Keys.Shift, device: 1).Down(30, device: 2);

        Assert.Equal("Aa", keys.Typed);
    }

    [Fact]
    public void LetsGoOfKeysAfterLostEventsAndAnUnpluggedKeyboard()
    {
        Keys keys = new Keys().Down(Keys.Shift, device: 1).Down(30, device: 2);

        IReadOnlyList<KeyStroke> resynced = keys.Translator.Resync(1, []);
        IReadOnlyList<KeyStroke> forgotten = keys.Translator.Forget(2);

        Assert.Equal([(false, Key.LeftShift)], resynced.Select(stroke => (stroke.Down, stroke.Key)));
        Assert.Equal([(false, Key.A)], forgotten.Select(stroke => (stroke.Down, stroke.Key)));
        Assert.Equal("a", keys.Tap(30).Typed[1..]);
    }

    // A key pressed while events were lost counts as held but types nothing; its release goes up.
    [Fact]
    public void TypesNothingForAKeyPressedWhileEventsWereLost()
    {
        Keys keys = new();

        Assert.Empty(keys.Translator.Resync(1, [30]));
        Assert.Equal("", keys.Up(30).Typed);
        Assert.Equal([(false, Key.A)], keys.Strokes.Select(stroke => (stroke.Down, stroke.Key)));
    }

    [Fact]
    public void IgnoresWhatIsNoKey()
    {
        KeyboardTranslator translator = new Keys().Translator;

        Assert.Empty(translator.Feed(1, new EvdevEvent(EvdevEvents.Sync, 0, 0)));
        Assert.Empty(translator.Feed(1, new EvdevEvent(EvdevEvents.Key, 0x110, 1)));
        Assert.Empty(translator.Feed(1, new EvdevEvent(EvdevEvents.Key, 30, 0)));
    }

    [Fact]
    public void MakesRightAltASecondAltWhereTheLayoutHasNoAltGr()
    {
        Keys keys = new Keys("us").Tap(16, Keys.AltGr);

        Assert.Equal("", keys.Typed);
        Assert.Equal(KeyModifiers.Alt, keys.Strokes.Single(stroke => stroke is { Down: true, Key: Key.Q }).Modifiers);
    }

    [Fact]
    public void DropsAWaitingDeadKeyWhenTheLayoutChanges()
    {
        Keys keys = new Keys().Tap(41);
        keys.Translator.Layout = Keys.Layout("us");

        Assert.Equal("y", keys.Tap(21).Typed);
    }
}
