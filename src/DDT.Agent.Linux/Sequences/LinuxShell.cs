// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Agent.Sequences;

// What a shell script finds in its environment on Linux whoever started the agent.
public static class LinuxShell
{
    public const string DefaultPath = "/usr/local/sbin:/usr/local/bin:/usr/sbin:/usr/bin:/sbin:/bin";

    public const string DefaultHome = "/root";

    // The agent's own PATH and HOME where it has them. An init system may start it with neither.
    public static IReadOnlyDictionary<string, string> Environment(Func<string, string?> variable)
    {
        ArgumentNullException.ThrowIfNull(variable);

        return new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["PATH"] = variable("PATH") is { Length: > 0 } path ? path : DefaultPath,
            ["HOME"] = variable("HOME") is { Length: > 0 } home ? home : DefaultHome,
        };
    }
}
