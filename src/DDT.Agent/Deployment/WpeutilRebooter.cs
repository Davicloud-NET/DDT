// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Agent.Deployment;

// The agent's exit does not restart Windows PE: startnet.cmd leaves a command prompt open.
public sealed class WpeutilRebooter(IToolRunner tools) : IRebooter
{
    public Task RebootAsync(CancellationToken cancellationToken) =>
        tools.RunAsync(Path.Combine(Environment.SystemDirectory, "wpeutil.exe"), ["reboot"], cancellationToken);
}
