// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Collections.Concurrent;
using System.Security.Cryptography.X509Certificates;
using DDT.Contracts;
using DDT.Contracts.Deployments;
using DDT.Contracts.Machines;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;

namespace DDT.E2E;

// A signed-in browser's connection to the live hub. It uses long polling, so every message the hub sends passes
// through a handler that keeps it, and it records the pushes for the machines it watches.
internal sealed class LiveRecorder : IAsyncDisposable
{
    private readonly ConcurrentQueue<string> _traffic = new();
    private readonly Lock _lock = new();
    private readonly List<RunStepChangedEvent> _steps = [];
    private readonly List<MachineLogAppendedEvent> _logPushes = [];
    private readonly HubConnection _connection;

    private LiveRecorder(AdminApi api, X509Certificate2 rootCertificate)
    {
        _connection = new HubConnectionBuilder()
            .WithUrl(new Uri(api.BaseAddress, "hubs/live"), options =>
            {
                options.Transports = HttpTransportType.LongPolling;
                options.HttpMessageHandlerFactory = _ => new RecordingHandler(AdminApi.Handler(rootCertificate, null), _traffic);
                options.Headers["Cookie"] = api.Cookies.GetCookieHeader(api.BaseAddress);
            })
            .AddJsonProtocol(json => json.PayloadSerializerOptions.TypeInfoResolverChain.Insert(0, DdtJsonContext.Default))
            .Build();

        _connection.On<RunStepChangedEvent>("runStepChanged", change => Add(_steps, change));
        _connection.On<MachineLogAppendedEvent>("machineLogAppended", push => Add(_logPushes, push));
    }

    // Everything the hub sent so far, as it came over the wire.
    public string Traffic => string.Join(Environment.NewLine, _traffic);

    public static async Task<LiveRecorder> ConnectAsync(AdminApi api, X509Certificate2 rootCertificate, CancellationToken cancellationToken)
    {
        LiveRecorder recorder = new(api, rootCertificate);
        await recorder._connection.StartAsync(cancellationToken).ConfigureAwait(false);

        return recorder;
    }

    public Task WatchAsync(Guid machineId, CancellationToken cancellationToken) =>
        _connection.InvokeAsync("WatchMachine", machineId, cancellationToken);

    public IReadOnlyList<MachineLogAppendedEvent> LogPushes(Guid machineId)
    {
        lock (_lock)
        {
            return [.. _logPushes.Where(push => push.MachineId == machineId)];
        }
    }

    public IReadOnlyList<DeploymentStepView> StepPushes(Guid machineId)
    {
        lock (_lock)
        {
            return [.. _steps.Where(change => change.MachineId == machineId).Select(change => change.Step)];
        }
    }

    public Task<MachineLogAppendedEvent> WaitForLogPushAsync(Guid machineId, TimeSpan timeout, Func<string> diagnostics, CancellationToken cancellationToken) =>
        Eventually.GetAsync(
            $"A machineLogAppended push for machine {machineId}",
            timeout,
            _ => Task.FromResult(LogPushes(machineId).FirstOrDefault()),
            diagnostics,
            cancellationToken);

    public async ValueTask DisposeAsync() => await _connection.DisposeAsync().ConfigureAwait(false);

    private void Add<T>(List<T> events, T item)
    {
        lock (_lock)
        {
            events.Add(item);
        }
    }
}
