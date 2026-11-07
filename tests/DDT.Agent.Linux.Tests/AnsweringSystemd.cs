// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Agent.Deployment;
using DDT.Agent.LinuxPhase;

namespace DDT.Agent.Tests;

// Stands in for systemd where a test runs the unit's own path: systemctl and reboot are noted and answered, and every
// other tool runs for real. A restart the agent asks for stops the unit, as systemd stops it on the machine's way down.
internal sealed class AnsweringSystemd(IToolRunner real, CancellationTokenSource unit) : IToolRunner
{
    private readonly Lock _lock = new();
    private readonly List<string> _calls = [];

    // Throws in place of an answer, for a systemd that is not there.
    public Exception? Failure { get; init; }

    public List<string> Calls
    {
        get
        {
            lock (_lock)
            {
                return [.. _calls];
            }
        }
    }

    public Task<IReadOnlyList<string>> RunAsync(string fileName, IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        if (fileName is not (LinuxPhaseRebooter.Systemctl or LinuxPhaseRebooter.Fallback))
        {
            return real.RunAsync(fileName, arguments, cancellationToken);
        }

        lock (_lock)
        {
            _calls.Add(ToolRunner.CommandLine(fileName, arguments));
        }

        if (Failure is { } failure)
        {
            throw failure;
        }

        if (fileName == LinuxPhaseRebooter.Fallback || arguments is ["reboot"])
        {
            unit.Cancel();
        }

        return Task.FromResult<IReadOnlyList<string>>([]);
    }

    public Task<int> RunForExitCodeAsync(string fileName, IReadOnlyList<string> arguments, ToolRunOptions options, CancellationToken cancellationToken) =>
        real.RunForExitCodeAsync(fileName, arguments, options, cancellationToken);
}
