// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Runtime.CompilerServices;
using DDT.Agent.WindowsPhase;
using Xunit;

namespace DDT.Agent.Tests;

// The service's side of the service control manager without one: what it reports, and how it answers controls.
public sealed class ServiceLifetimeTests
{
    private readonly Lock _lock = new();
    private readonly List<ServiceStatus> _reports = [];

    private List<ServiceStatus> Reports
    {
        get
        {
            lock (_lock)
            {
                return [.. _reports];
            }
        }
    }

    [Fact]
    public void TheStatusIsSevenDwordsAsSetServiceStatusReadsThem()
    {
        Assert.Equal(28, Unsafe.SizeOf<ServiceStatus>());
    }

    [Fact]
    public async Task RunsTheBodyAndReportsRunningThenStopped()
    {
        ServiceLifetime lifetime = Lifetime();

        int exitCode = await lifetime.RunAsync(_ => Task.FromResult(0));

        Assert.Equal(0, exitCode);
        Assert.Equal(
            [
                new ServiceStatus(ServiceLifetime.OwnProcess, ServiceLifetime.StateRunning, ServiceLifetime.AcceptStop | ServiceLifetime.AcceptShutdown, 0, 0, 0, 0),
                new ServiceStatus(ServiceLifetime.OwnProcess, ServiceLifetime.StateStopped, 0, 0, 0, 0, 0),
            ],
            Reports);
    }

    [Theory]
    [InlineData(ServiceLifetime.ControlStop)]
    [InlineData(ServiceLifetime.ControlShutdown)]
    public async Task AStopOrShutdownCancelsTheBodyWhileStopPendingIsReported(uint control)
    {
        ServiceLifetime lifetime = Lifetime();
        TaskCompletionSource started = new(TaskCreationOptions.RunContinuationsAsynchronously);

        Task<int> running = lifetime.RunAsync(async cancellationToken =>
        {
            started.SetResult();

            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }
            catch (OperationCanceledException)
            {
            }

            return 0;
        });

        await started.Task.WaitAsync(TestContext.Current.CancellationToken);

        Assert.Equal(ServiceLifetime.NoError, lifetime.Control(control));
        Assert.Equal(0, await running.WaitAsync(TestContext.Current.CancellationToken));

        List<ServiceStatus> reports = Reports;
        Assert.Equal(
            [ServiceLifetime.StateRunning, ServiceLifetime.StateStopPending, ServiceLifetime.StateStopped],
            reports.Select(report => report.CurrentState));
        Assert.Equal((0u, 1u, ServiceLifetime.StopWaitHintMilliseconds), (reports[1].ControlsAccepted, reports[1].CheckPoint, reports[1].WaitHint));
    }

    [Fact]
    public void AnswersAnInterrogationAndRefusesWhatItDoesNotAccept()
    {
        ServiceLifetime lifetime = Lifetime();

        Assert.Equal(ServiceLifetime.NoError, lifetime.Control(ServiceLifetime.ControlInterrogate));
        Assert.Equal(ServiceLifetime.ErrorCallNotImplemented, lifetime.Control(2));
        Assert.Empty(Reports);
    }

    [Fact]
    public async Task AnExitCodeOtherThanZeroIsTheServicesOwn()
    {
        int exitCode = await Lifetime().RunAsync(_ => Task.FromResult(AgentExitCodes.ConfigurationError));

        Assert.Equal(AgentExitCodes.ConfigurationError, exitCode);
        ServiceStatus stopped = Reports[^1];
        Assert.Equal(
            (ServiceLifetime.StateStopped, ServiceLifetime.ErrorServiceSpecificError, (uint)AgentExitCodes.ConfigurationError),
            (stopped.CurrentState, stopped.Win32ExitCode, stopped.ServiceSpecificExitCode));
    }

    // Once Stopped is reported, the dispatcher may return on the main thread, where the host reads the exit code, before
    // RunAsync returns.
    [Fact]
    public async Task TheExitCodeIsKnownWhenStoppedIsReported()
    {
        (int ExitCode, int WhenStopped) returned = await ExitCodeWhenStoppedAsync(_ => Task.FromResult(AgentExitCodes.ConfigurationError));
        (int ExitCode, int WhenStopped) thrown = await ExitCodeWhenStoppedAsync(_ => throw new InvalidOperationException("broken"));

        Assert.Equal((AgentExitCodes.ConfigurationError, AgentExitCodes.ConfigurationError), returned);
        Assert.Equal(((int)ServiceLifetime.ErrorExceptionInService, (int)ServiceLifetime.ErrorExceptionInService), thrown);
    }

    [Fact]
    public async Task AnExceptionFromTheBodyStopsTheServiceAsFailedInsteadOfLeavingIt()
    {
        int exitCode = await Lifetime().RunAsync(_ => throw new InvalidOperationException("broken"));

        Assert.Equal((int)ServiceLifetime.ErrorExceptionInService, exitCode);
        ServiceStatus stopped = Reports[^1];
        Assert.Equal((ServiceLifetime.StateStopped, ServiceLifetime.ErrorExceptionInService, 0u), (stopped.CurrentState, stopped.Win32ExitCode, stopped.ServiceSpecificExitCode));
    }

    [Fact]
    public async Task AStopAfterTheServiceStoppedReportsNothing()
    {
        ServiceLifetime lifetime = Lifetime();
        await lifetime.RunAsync(_ => Task.FromResult(0));

        Assert.Equal(ServiceLifetime.NoError, lifetime.Control(ServiceLifetime.ControlStop));
        Assert.Equal(ServiceLifetime.StateStopped, Reports[^1].CurrentState);
        Assert.Equal(2, Reports.Count);
    }

    private static async Task<(int ExitCode, int WhenStopped)> ExitCodeWhenStoppedAsync(Func<CancellationToken, Task<int>> body)
    {
        ServiceLifetime? lifetime = null;
        int whenStopped = -1;
        lifetime = new ServiceLifetime(status =>
        {
            if (status.CurrentState == ServiceLifetime.StateStopped)
            {
                whenStopped = lifetime!.ExitCode;
            }
        });

        int exitCode = await lifetime.RunAsync(body);

        return (exitCode, whenStopped);
    }

    private ServiceLifetime Lifetime() => new(status =>
    {
        lock (_lock)
        {
            _reports.Add(status);
        }
    });
}
