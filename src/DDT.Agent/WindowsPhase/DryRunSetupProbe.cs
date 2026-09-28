// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Agent.WindowsPhase;

// A dry run's Windows was never set up, so its setup counts as finished right away.
public sealed class DryRunSetupProbe(AgentLog log) : IWindowsSetupProbe
{
    public string? Pending()
    {
        log.Information(
            "Dry run: Windows setup counts as finished. In Windows the agent would wait until SystemSetupInProgress and " +
            $"OOBEInProgress are 0 and the image state is {RegistrySetupProbe.Complete}.");

        return null;
    }
}
