// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.ComponentModel;
using System.Diagnostics;

namespace DDT.Agent.Deployment;

// A tool the agent runs as itself, through .NET's Process. It's an IToolProcess so the run-as path and the agent path
// share the same output handling and timeout.
internal sealed class ProcessToolProcess : IToolProcess
{
    private readonly Process _process;
    private readonly AgentLog _log;
    private readonly string _tool;
    private readonly IToolStop? _stop;

    // The process's input is already closed by ToolRunner.Start, so no tool waits on a question. stop asks the tool
    // to end first and ends what it started outside its own tree, where the system has a way to.
    public ProcessToolProcess(Process process, AgentLog log, string tool, IToolStop? stop = null)
    {
        _process = process;
        _log = log;
        _tool = tool;
        _stop = stop;
    }

    public Stream StandardOutput => _process.StandardOutput.BaseStream;

    public Stream StandardError => _process.StandardError.BaseStream;

    public int ExitCode => _process.ExitCode;

    public Task WaitForExitAsync(CancellationToken cancellationToken) => _process.WaitForExitAsync(cancellationToken);

    public void Kill()
    {
        _stop?.Ask(_tool);

        try
        {
            _process.Kill(entireProcessTree: true);
        }
        catch (Exception exception) when (exception is InvalidOperationException or Win32Exception)
        {
            _log.Warning($"{_tool} could not be stopped ({exception.Message}).");
        }

        _stop?.End();
    }

    public void Dispose() => _process.Dispose();
}
