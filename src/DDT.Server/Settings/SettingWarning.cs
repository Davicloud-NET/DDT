// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Messages;

namespace DDT.Server.Settings;

// Field is a path inside the section, the same as in a SettingProblem. A save has to confirm the warnings whose Code
// is in SettingWarningCodes.NeedConfirmation. Any other warning only informs.
public sealed record SettingWarning(string Field, ServerMessage Text, string? Code)
{
    public string Message => Text.Text;

    // Compares by the English text, because Text's Args is a dictionary, and a dictionary only compares by reference.
    public bool Equals(SettingWarning? other) =>
        other is not null && Field == other.Field && Message == other.Message && Code == other.Code;

    public override int GetHashCode() => HashCode.Combine(Field, Message, Code);
}
