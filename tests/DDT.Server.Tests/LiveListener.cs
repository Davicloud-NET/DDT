// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Threading.Channels;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DDT.Server.Tests;

// The live hub as a signed-in browser sees it. Events are recorded from the moment they are listened for.
public sealed class LiveListener : IAsyncDisposable
{
    private readonly HubConnection _connection;

    private LiveListener(HubConnection connection) => _connection = connection;

    public static async Task<LiveListener> StartAsync(DdtApplication application, SignedInClient client)
    {
        ArgumentNullException.ThrowIfNull(application);
        ArgumentNullException.ThrowIfNull(client);

        HubConnection connection = new HubConnectionBuilder()
            .WithUrl(new Uri(application.Server.BaseAddress, "hubs/live"), options =>
            {
                options.Transports = HttpTransportType.LongPolling;
                options.HttpMessageHandlerFactory = _ => application.Server.CreateHandler();
                options.Headers["Cookie"] = client.Cookies.GetCookieHeader(application.Server.BaseAddress);
            })
            .AddJsonProtocol(json => json.PayloadSerializerOptions = TestJson.Options)
            .Build();

        await connection.StartAsync(TestContext.Current.CancellationToken);

        return new LiveListener(connection);
    }

    public ChannelReader<T> Listen<T>(string liveEvent)
    {
        Channel<T> received = Channel.CreateUnbounded<T>();
        _connection.On<T>(liveEvent, payload => received.Writer.TryWrite(payload));

        return received.Reader;
    }

    // For events without a payload; each one is recorded as the time it arrived.
    public ChannelReader<DateTimeOffset> Listen(string liveEvent)
    {
        Channel<DateTimeOffset> received = Channel.CreateUnbounded<DateTimeOffset>();
        _connection.On(liveEvent, () => received.Writer.TryWrite(DateTimeOffset.UtcNow));

        return received.Reader;
    }

    public static async Task<T> NextAsync<T>(ChannelReader<T> events, Func<T, bool>? expected = null)
    {
        ArgumentNullException.ThrowIfNull(events);

        using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));

        await foreach (T received in events.ReadAllAsync(timeout.Token))
        {
            if (expected is null || expected(received))
            {
                return received;
            }
        }

        throw new InvalidOperationException("The hub closed.");
    }

    public Task WatchAsync(Guid machineId) => _connection.InvokeAsync("WatchMachine", machineId, TestContext.Current.CancellationToken);

    public Task UnwatchAsync(Guid machineId) => _connection.InvokeAsync("UnwatchMachine", machineId, TestContext.Current.CancellationToken);

    public ValueTask DisposeAsync() => _connection.DisposeAsync();
}
