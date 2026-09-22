// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Agent.WindowsPhase;

// What the service tells the service control manager, through report, and when: Running, accepting stop and shutdown,
// while the body runs; Stop pending once either arrives, which cancels the body; Stopped with the body's exit code once
// it returns. Controls arrive on the control manager's own thread and are answered at once, so the body's cancellation
// callbacks run elsewhere.
public sealed class ServiceLifetime(Action<ServiceStatus> report)
{
    public const uint OwnProcess = 0x10;

    public const uint StateStopped = 1;
    public const uint StateStopPending = 3;
    public const uint StateRunning = 4;

    public const uint AcceptStop = 0x1;
    public const uint AcceptShutdown = 0x4;

    public const uint ControlStop = 1;
    public const uint ControlInterrogate = 4;
    public const uint ControlShutdown = 5;

    public const uint NoError = 0;
    public const uint ErrorCallNotImplemented = 120;
    public const uint ErrorExceptionInService = 1064;
    public const uint ErrorServiceSpecificError = 1066;

    // How long the control manager waits for the next report while the body winds down.
    public const uint StopWaitHintMilliseconds = 30_000;

    private readonly Lock _lock = new();
    private readonly CancellationTokenSource _stop = new();
    private uint _state;
    private uint _checkPoint;
    private int _exitCode;

    // What RunAsync returns, set before Stopped is reported: from then on the dispatcher may return on another thread,
    // which reads it here, and the process may end at any time.
    public int ExitCode
    {
        get
        {
            lock (_lock)
            {
                return _exitCode;
            }
        }
    }

    // Runs body until it returns. An exception it throws ends the service as failed, as it must never reach the
    // control manager's thread; the body logs it, as nothing here can.
    public async Task<int> RunAsync(Func<CancellationToken, Task<int>> body)
    {
        ArgumentNullException.ThrowIfNull(body);

        Report(StateRunning, AcceptStop | AcceptShutdown, NoError, 0, 0);

        int exitCode;
        uint win32ExitCode;

        try
        {
            exitCode = await body(_stop.Token).ConfigureAwait(false);
            win32ExitCode = exitCode == 0 ? NoError : ErrorServiceSpecificError;
        }
        catch (Exception)
        {
            exitCode = (int)ErrorExceptionInService;
            win32ExitCode = ErrorExceptionInService;
        }

        lock (_lock)
        {
            _exitCode = exitCode;
        }

        Report(StateStopped, 0, win32ExitCode, win32ExitCode == ErrorServiceSpecificError ? (uint)exitCode : 0, 0);

        return exitCode;
    }

    // Returns the answer for the control manager.
    public uint Control(uint control)
    {
        switch (control)
        {
            case ControlStop:
            case ControlShutdown:
                lock (_lock)
                {
                    if (_state == StateRunning)
                    {
                        Report(StateStopPending, 0, NoError, 0, StopWaitHintMilliseconds);
                    }
                }

                // The callbacks run on the thread pool, not on the control manager's thread.
                _ = _stop.CancelAsync();

                return NoError;
            case ControlInterrogate:
                return NoError;
            default:
                return ErrorCallNotImplemented;
        }
    }

    private void Report(uint state, uint accepted, uint win32ExitCode, uint serviceExitCode, uint waitHint)
    {
        lock (_lock)
        {
            // Once stopped, the service says nothing more.
            if (_state == StateStopped)
            {
                return;
            }

            _state = state;
            _checkPoint = state == StateStopPending ? _checkPoint + 1 : 0;
            report(new ServiceStatus(OwnProcess, state, accepted, win32ExitCode, serviceExitCode, _checkPoint, waitHint));
        }
    }
}
