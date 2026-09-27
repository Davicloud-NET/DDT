// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Messages;

namespace DDT.Core.Configuration;

// Field is the path of the setting inside its section, such as Domain:UserName, and Text is worded for someone looking
// at that field, as a code from ServerMessages with its values, so the settings page can say it in the person's
// language. Message is its English, which the startup check and the logs print.
//
// Two problems are equal when they say the same at the same place: the code and its values say it again, and a
// dictionary compares only by reference.
public sealed record SettingProblem(string Field, ServerMessage Text)
{
    public string Message => Text.Text;

    // At startup nobody is looking at a field, so the line names the whole configuration key.
    public string Describe(string section) => $"{section}:{Field}: {Message}";

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
