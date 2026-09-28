// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Agent.Consoles;

namespace DDT.Agent.Tests;

// Starts program, standing in for ddt-console.exe, as a process would: Exited completes with what it returns, or with
// -1 once Stop ended it, and an exception it throws is a crash.
internal sealed class FakeConsoleLauncher(Func<string, CancellationToken, Task<int>> program) : IConsoleLauncher
{
    private readonly List<FakeConsoleProcess> _started = [];

    public FakeConsoleProcess? Started
    {
        get
        {
            lock (_started)
            {
                return _started.SingleOrDefault();
            }
        }
    }

    public IConsoleProcess Start(string pipeName)
    {
        FakeConsoleProcess process = new(program, pipeName);

        lock (_started)
        {
            _started.Add(process);
        }

        return process;
    }
}
