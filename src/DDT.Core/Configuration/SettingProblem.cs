// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Messages;

namespace DDT.Core.Configuration;

// Field is the setting's path in its section, such as Domain:UserName. Text is worded for someone at that field, as a
// code the settings page says in the person's language; Message is its English, for the startup check and the logs.
public sealed record SettingProblem(string Field, ServerMessage Text)
{
    public string Message => Text.Text;

    // At startup nobody is looking at a field, so the line names the whole configuration key.
    public string Describe(string section) => $"{section}:{Field}: {Message}";

    // The same said at the same place. The message's text stands in for its values, a dictionary compared by reference.
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
