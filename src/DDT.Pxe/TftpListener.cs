// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using DDT.Protocols.Tftp;
using Microsoft.Extensions.Logging;

namespace DDT.Pxe;

// Each read request on port 69 becomes a TftpTransfer on a separate task, so one slow client can't stall the rest. A
// transfer answers from its own port. In single port mode it answers from port 69, and this loop routes its datagrams.
public sealed class TftpListener : IPxeListener, IAsyncDisposable
{
    private const int ReceiveBufferLength = 1024;

    private readonly IPEndPoint _bindTo;
    private readonly NetworkInterfaceMap _interfaces;
    private readonly BootFiles _files;
    private readonly TftpServing _serving;
    private readonly TftpTransferServices _transferServices;
    private readonly ILogger _logger;
    private readonly CancellationTokenSource _stopping = new();
    private readonly ConcurrentDictionary<TftpTransfer, IPEndPoint> _transfers = new();

    // Only used in single port mode, where the listener routes each client's datagrams to its transfer.
    private readonly ConcurrentDictionary<IPEndPoint, TftpTransfer> _singlePortClients = new();

    private Socket? _socket;
    private Task _loop = Task.CompletedTask;

    public TftpListener(
        IPEndPoint bindTo,
        NetworkInterfaceMap interfaces,
        BootFiles files,
        TftpServing serving,
        TimeProvider timeProvider,
        ILogger<TftpListener> logger)
    {
        ArgumentNullException.ThrowIfNull(bindTo);
        ArgumentNullException.ThrowIfNull(interfaces);
        ArgumentNullException.ThrowIfNull(files);
        ArgumentNullException.ThrowIfNull(serving);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(logger);

        _bindTo = bindTo;
        _interfaces = interfaces;
        _files = files;
        _serving = serving;
        _transferServices = new TftpTransferServices(serving.Limits, timeProvider, logger);
        _logger = logger;
    }

    public IPEndPoint LocalEndPoint =>
        (IPEndPoint?)_socket?.LocalEndPoint ?? throw new InvalidOperationException("The listener has not started.");

    public void Start()
    {
        _socket = PxeSocket.CreateListener(_bindTo);
        _loop = RunAsync(_socket, _stopping.Token);
    }

    public async Task StopAsync()
    {
        await _stopping.CancelAsync().ConfigureAwait(false);
        await _loop.ConfigureAwait(false);

        // In single port mode the transfers tell their clients about the shutdown through this socket.
        await Task.WhenAll(_transfers.Keys.Select(transfer => transfer.Completion)).ConfigureAwait(false);
        _socket?.Dispose();
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
        _stopping.Dispose();
    }

    private async Task RunAsync(Socket socket, CancellationToken cancellationToken)
    {
        byte[] buffer = new byte[ReceiveBufferLength];
        bool seenFirst = false;

        while (!cancellationToken.IsCancellationRequested)
        {
            if (await PxeSocket.ReceiveAsync(socket, buffer, _bindTo.Port, _logger, cancellationToken).ConfigureAwait(false)
                is not { } received)
            {
                return;
            }

            IPEndPoint client = (IPEndPoint)received.RemoteEndPoint;
            IPAddress localAddress = received.PacketInformation.Address;
            int port = ((IPEndPoint)socket.LocalEndPoint!).Port;

            // RFC 1123 section 4.2.3.5: a request sent to a broadcast address is ignored. Requiring the
            // destination to be a served address covers that and the interface allowlist in one test.
            if (!_interfaces.TryGetInterface(received.PacketInformation.Interface, out ServedInterface? served)
                || !served.Owns(localAddress))
            {
                PxeLog.InterfaceNotServed(_logger, client, port, received.PacketInformation.Interface);
                continue;
            }

            if (!seenFirst)
            {
                seenFirst = true;
                PxeLog.FirstDatagram(_logger, port, client, served.Name);
            }

            try
            {
                await AcceptAsync(socket, buffer.AsMemory(0, received.ReceivedBytes), localAddress, client, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                PxeLog.DatagramFailed(_logger, _bindTo.Port, exception);
            }
        }
    }

    private async Task AcceptAsync(
        Socket socket,
        ReadOnlyMemory<byte> datagram,
        IPAddress localAddress,
        IPEndPoint client,
        CancellationToken cancellationToken)
    {
        if (!TftpPacket.TryReadOpcode(datagram.Span, out TftpOpcode opcode))
        {
            return;
        }

        if (opcode != TftpOpcode.ReadRequest)
        {
            if (!RouteToRunning(client, datagram.Span) && opcode == TftpOpcode.WriteRequest)
            {
                PxeLog.TftpRefusedOperation(_logger, opcode, client);
                using ITftpTransport refusal = CreateTransport(socket, localAddress, client);
                await SendErrorAsync(refusal, TftpErrorCode.IllegalOperation, "Write requests are not supported", cancellationToken)
                    .ConfigureAwait(false);
            }

            return;
        }

        if (!TftpPacket.TryReadReadRequest(datagram.Span, out TftpReadRequest? request)
            || !request.Mode.Equals("octet", StringComparison.OrdinalIgnoreCase))
        {
            PxeLog.TftpRefusedOperation(_logger, opcode, client);
            using ITftpTransport refusal = CreateTransport(socket, localAddress, client);
            await SendErrorAsync(refusal, TftpErrorCode.IllegalOperation, "Only octet mode reads are supported", cancellationToken)
                .ConfigureAwait(false);

            return;
        }

        AcceptRead(socket, request, localAddress, client, cancellationToken);
    }

    // Nothing here looks at the disk or waits: this is the loop every machine's request comes through.
    private void AcceptRead(
        Socket socket,
        TftpReadRequest request,
        IPAddress localAddress,
        IPEndPoint client,
        CancellationToken cancellationToken)
    {
        if (!ReplacePrevious(client))
        {
            return;
        }

        // Dropped instead of refused. An ERROR aborts the client's boot, while a dropped request is retransmitted and
        // succeeds once a transfer finishes.
        if (_transfers.Count >= _serving.MaxTransfers)
        {
            PxeLog.TftpBusy(_logger, request.FileName, client, _serving.MaxTransfers);

            return;
        }

        if (_files.Starting(request.FileName, client.Address) is > 0 and int reset)
        {
            PxeLog.BootConnectionsReset(_logger, reset, client.Address);
        }

        BootRequest asked = new(localAddress, client.Address);

        StartTransfer(CreateTransport(socket, localAddress, client), client, () => Find(request.FileName, asked, client), request, cancellationToken);
    }

    // Runs in the transfer, since it looks at the disk. Null when there is no such file to send.
    private BootContent? Find(string requested, BootRequest asked, IPEndPoint client)
    {
        if (!_files.TryFind(requested, asked, out BootContent? content))
        {
            LogNotFound(requested, client);

            return null;
        }

        PxeLog.TftpRead(_logger, requested, client, content.Description);

        return content;
    }

    // shim and GRUB ask for a handful of files at every start that DDT does not have, and go on without them.
    private void LogNotFound(string requested, IPEndPoint client)
    {
        if (BootFiles.Normalized(requested) is { } name && BootProbes.IsProbe(name))
        {
            PxeLog.TftpProbe(_logger, requested, client);
        }
        else
        {
            PxeLog.TftpNotFound(_logger, requested, client);
        }
    }

    // In single port mode every datagram from a client with a running transfer goes to it, except a new read request.
    private bool RouteToRunning(IPEndPoint client, ReadOnlySpan<byte> datagram)
    {
        if (_singlePortClients.TryGetValue(client, out TftpTransfer? running) && running.Transport is SharedPortTftpTransport shared)
        {
            shared.Deliver(datagram);

            return true;
        }

        return false;
    }

    // A repeated request means no answer has reached the client yet. With a port per transfer, it gets its own transfer
    // and the client keeps the port it heard first. In single port mode it replaces an unanswered transfer. False means
    // the request is ignored.
    private bool ReplacePrevious(IPEndPoint client)
    {
        if (_singlePortClients.TryGetValue(client, out TftpTransfer? previous))
        {
            if (previous.ClientAnswered)
            {
                return false;
            }

            previous.Cancel();
        }

        return true;
    }

    private void StartTransfer(
        ITftpTransport transport,
        IPEndPoint client,
        Func<BootContent?> find,
        TftpReadRequest request,
        CancellationToken cancellationToken)
    {
        TftpTransfer transfer = new(transport, client, find, request, _transferServices, cancellationToken);

        _transfers[transfer] = client;

        if (_serving.SinglePort)
        {
            _singlePortClients[client] = transfer;
        }

        _ = transfer.Start().ContinueWith(
            _ =>
            {
                _transfers.TryRemove(transfer, out IPEndPoint? _);
                _singlePortClients.TryRemove(new KeyValuePair<IPEndPoint, TftpTransfer>(client, transfer));
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    private ITftpTransport CreateTransport(Socket socket, IPAddress localAddress, IPEndPoint client) =>
        _serving.SinglePort
            ? new SharedPortTftpTransport(socket, client)
            : new ConnectedTftpTransport(PxeSocket.CreateTransfer(localAddress, client));

    private static async Task SendErrorAsync(
        ITftpTransport transport,
        TftpErrorCode code,
        string message,
        CancellationToken cancellationToken)
    {
        byte[] packet = new byte[TftpPacket.HeaderLength + message.Length + 1];

        if (!TftpPacket.TryWriteError(packet, code, message, out int length))
        {
            return;
        }

        try
        {
            await transport.SendAsync(packet.AsMemory(0, length), cancellationToken).ConfigureAwait(false);
        }
        catch (SocketException exception) when (PxeSocket.IsTransient(exception))
        {
        }
    }
}
