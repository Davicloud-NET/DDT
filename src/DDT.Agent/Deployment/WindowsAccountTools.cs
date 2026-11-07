// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Agent.Deployment;

// The accounts of a run's steps through Windows: its share connections and its sign-in.
public static class WindowsAccountTools
{
    public static AccountTools Create(AgentLog log) => new(new ShareConnector(new WNetConnections(), log), new WindowsAccountLogons(log));

    // A dry run connects no share and signs nobody in.
    public static AccountTools DryRun(AgentLog log) => new(new DryRunShareConnector(log), new DryRunAccountLogons(log));
}
