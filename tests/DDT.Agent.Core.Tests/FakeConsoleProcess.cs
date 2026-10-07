// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Agent.Consoles;
using DDT.ConsoleProtocol;

namespace DDT.Agent.Tests;

internal sealed class FakeConsoleProcess : IConsoleProcess
{
    private readonly CancellationTokenSource _killed = new();

    public FakeConsoleProcess(Func<string, CancellationToken, Task<int>> program, string pipeName)
    {
        PipeName = pipeName;
        Exited = RunAsync(program, pipeName);
    }

    public string PipeName { get; }

    public Task<int> Exited { get; }

    public bool Stopped { get; private set; }

    public void Stop()
    {
        Stopped = true;
        _killed.Cancel();
    }

    public void Dispose()
    {
    }

    private async Task<int> RunAsync(Func<string, CancellationToken, Task<int>> program, string pipeName)
    {
        await Task.Yield();

        try
        {
            return await program(pipeName, _killed.Token);
        }
        catch (OperationCanceledException) when (_killed.IsCancellationRequested)
        {
            return -1;
        }
        catch (Exception exception) when (exception is IOException or ConsoleProtocolException or TimeoutException)
        {
            return FakeGraphicalConsole.CrashExitCode;
        }
    }
}
