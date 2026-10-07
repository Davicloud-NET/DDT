// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Agent.Consoles;

// A started console. Exited completes with its exit code once it has ended. Stop ends it, whether or not it still runs.
public interface IConsoleProcess : IDisposable
{
    Task<int> Exited { get; }

    void Stop();
}
