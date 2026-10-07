// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Agent.Deployment;

// A tool ToolRunner started, as the agent or as an account, with its input closed and its output in pipes.
public interface IToolProcess : IDisposable
{
    Stream StandardOutput { get; }

    Stream StandardError { get; }

    // Valid once WaitForExitAsync completed.
    int ExitCode { get; }

    Task WaitForExitAsync(CancellationToken cancellationToken);

    // Ends the tool and every process it started. A failure is logged, not thrown.
    void Kill();
}
