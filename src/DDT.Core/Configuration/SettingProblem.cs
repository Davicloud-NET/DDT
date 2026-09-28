// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Messages;

namespace DDT.Core.Configuration;

// Field is the setting's path in its section, such as Domain:UserName. Text is written for someone looking at that
// field. It's a code, so the settings page can show it in the user's language. Message is the English text, for the
// startup check and the logs.
public sealed record SettingProblem(string Field, ServerMessage Text)
{
    public string Message => Text.Text;

    // At startup nobody is looking at a field, so the line names the whole configuration key.
    public string Describe(string section) => $"{section}:{Field}: {Message}";

    // Equal when the same thing is said about the same field. It compares the message's text instead of its values,
    // because the values are a dictionary and would compare by reference.
    public bool Equals(SettingProblem? other) => other is not null && Field == other.Field && Message == other.Message;

    public override int GetHashCode() => HashCode.Combine(Field, Message);

    public static void ThrowIfAny(string section, IReadOnlyList<SettingProblem> problems)
    {
        ArgumentNullException.ThrowIfNull(problems);

        if (problems.Count > 0)
        {
            throw new InvalidOperationException(
                $"{section} is not valid:{Environment.NewLine}{string.Join(Environment.NewLine, problems.Select(problem => problem.Describe(section)))}");
        }
    }
}
