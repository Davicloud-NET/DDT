// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Core.Configuration;

// Field is the path of the setting inside its section, such as Domain:UserName, and Message is worded for someone
// looking at that field.
public sealed record SettingProblem(string Field, string Message)
{
    // At startup nobody is looking at a field, so the line names the whole configuration key.
    public string Describe(string section) => $"{section}:{Field}: {Message}";

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
