// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Messages;

namespace DDT.Server.Settings;

// Field is a path inside the section, as a SettingProblem's is. Code names a warning a save has to confirm; a warning
// without one only informs.
public sealed record SettingWarning(string Field, ServerMessage Text, string? Code)
{
    public string Message => Text.Text;

    // By the English text, because Text's Args is a dictionary, which compares only by reference.
    public bool Equals(SettingWarning? other) =>
        other is not null && Field == other.Field && Message == other.Message && Code == other.Code;

    public override int GetHashCode() => HashCode.Combine(Field, Message, Code);
}
