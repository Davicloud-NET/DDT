// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.ConsoleProtocol;
using DDT.MachineConsole.Texts;

namespace DDT.MachineConsole.ViewModels;

// What fills the space between the header and the footer: a stage of the agent, or the question it asks.
public abstract class ScreenViewModel(Localizer localizer) : ObservableObject
{
    protected Localizer L => localizer;

    // Everything the screen says, again, in the language now chosen.
    public virtual void Refresh() => RaiseAll();

    protected string T(string message) => localizer.T(message);

    protected string F(string message, params ReadOnlySpan<(string Name, string Value)> values) => localizer.F(message, values);
}

// A screen that shows a stage of the agent and follows its state.
public abstract class StageViewModel(Localizer localizer) : ScreenViewModel(localizer)
{
    public abstract void Update(ConsoleState state);
}

// How a state tag looks: filled for what is under way or asks for someone, outlined for what rests.
public enum TagTone
{
    Run,
    Attention,
    Fail,
    Ok,
    Idle,
}

// A fact about the machine, such as its MAC address. Mono sets it in the face for identifiers.
public sealed record Fact(string Label, string Value, bool Mono = false);

// A state tag: the text as it is set, in capitals, and its tone.
public sealed record Tag(string Text, TagTone Tone)
{
    public bool IsRun => Tone == TagTone.Run;

    public bool IsAttention => Tone == TagTone.Attention;

    public bool IsFail => Tone == TagTone.Fail;

    public bool IsOk => Tone == TagTone.Ok;

    public bool IsIdle => Tone == TagTone.Idle;

    public static Tag Of(string text, TagTone tone) => new(Say.Tag(text), tone);
}
