// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.MachineConsole.Texts;

namespace DDT.MachineConsole.ViewModels;

// What a StateTag shows. Of sets the text in capitals, as every tag is set.
public sealed record Tag(string Text, TagTone Tone)
{
    public bool IsRun => Tone == TagTone.Run;

    public bool IsAttention => Tone == TagTone.Attention;

    public bool IsFail => Tone == TagTone.Fail;

    public bool IsOk => Tone == TagTone.Ok;

    public bool IsIdle => Tone == TagTone.Idle;

    public static Tag Of(string text, TagTone tone) => new(Say.Tag(text), tone);
}
