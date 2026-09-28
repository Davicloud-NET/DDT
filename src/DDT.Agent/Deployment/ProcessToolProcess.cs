// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.ComponentModel;
using System.Diagnostics;

namespace DDT.Agent.Deployment;

// The tool the agent runs itself, through .NET's Process, as one IToolProcess so the run-as path and the agent path
// share the same output handling and timeout.
internal sealed class ProcessToolProcess : IToolProcess
{
    private readonly Process _process;
    private readonly AgentLog _log;

    // The process's input is already closed by ToolRunner.Start, so no tool waits on a question.
    public ProcessToolProcess(Process process, AgentLog log)
    {
        _process = process;
        _log = log;
    }

    public Stream StandardOutput => _process.StandardOutput.BaseStream;

    public Stream StandardError => _process.StandardError.BaseStream;

    public int ExitCode => _process.ExitCode;

    public Task WaitForExitAsync(CancellationToken cancellationToken) => _process.WaitForExitAsync(cancellationToken);

    public void Kill()
    {
        try
        {
            _process.Kill(entireProcessTree: true);
        }
        catch (Exception exception) when (exception is InvalidOperationException or Win32Exception)
        {
            _log.Warning($"{_process.ProcessName} could not be stopped ({exception.Message}).");
        }
    }

    public void Dispose() => _process.Dispose();
}
