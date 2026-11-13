// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Agents;

namespace DDT.Agent.InstalledPhase;

// What the log says of the first-logon settings. Never the password.
public static class FirstLogonTexts
{
    public static string Account(AgentAutoLogon autoLogon)
    {
        ArgumentNullException.ThrowIfNull(autoLogon);

        return autoLogon.Domain is { } domain ? $@"{domain}\{autoLogon.UserName}" : autoLogon.UserName;
    }

    public static string Summary(AgentFirstLogon firstLogon)
    {
        ArgumentNullException.ThrowIfNull(firstLogon);

        string commands = $"{firstLogon.Commands.Count} first-logon command{(firstLogon.Commands.Count == 1 ? "" : "s")}";

        return firstLogon.AutoLogon is { } autoLogon
            ? $"{commands} and the automatic logon of {Account(autoLogon)}, {autoLogon.Count} times"
            : commands;
    }
}
