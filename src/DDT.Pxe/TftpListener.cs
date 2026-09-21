// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using DDT.Protocols.Tftp;
using Microsoft.Extensions.Logging;

namespace DDT.Pxe;

// Port 69 accepts read requests and each one becomes a TftpTransfer running on its own task, so one
// slow client cannot stall the rest. By default a transfer answers from its own ephemeral port. In
// single port mode it answers from port 69 and this loop routes the client's datagrams to it.
public sealed class TftpListener : IAsyncDisposable
{
    private const int ReceiveBufferLength = 1024;

    private readonly IPEndPoint _bindTo;
    private readonly NetworkInterfaceMap _interfaces;
    private readonly BootFileResolver _files;
    private readonly TftpLimits _limits;
    private readonly int _maxTransfers;
    private readonly bool _singlePort;
    private readonly TimeProvider _timeProvider;
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
        BootFileResolver files,
        TftpLimits limits,
        int maxTransfers,
        bool singlePort,
        TimeProvider timeProvider,
        ILogger<TftpListener> logger)
    {
        ArgumentNullException.ThrowIfNull(bindTo);
        ArgumentNullException.ThrowIfNull(interfaces);
        ArgumentNullException.ThrowIfNull(files);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(logger);

        _bindTo = bindTo;
        _interfaces = interfaces;
        _files = files;
        _limits = limits;
        _maxTransfers = maxTransfers;
        _singlePort = singlePort;
        _timeProvider = timeProvider;
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
        EndPoint anySource = new IPEndPoint(IPAddress.Any, 0);
        bool seenFirst = false;

        while (!cancellationToken.IsCancellationRequested)
        {
            SocketReceiveMessageFromResult received;

            try
            {
                received = await socket.ReceiveMessageFromAsync(buffer, SocketFlags.None, anySource, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (ObjectDisposedException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (SocketException exception) when (PxeSocket.IsTransient(exception))
            {
                continue;
            }
            catch (SocketException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (SocketException exception)
            {
                // Never let the loop end: a dead listener leaves every machine unable to boot.
                PxeLog.ReceiveFailed(_logger, _bindTo.Port, exception);

                if (!await PxeSocket.PauseAsync(cancellationToken).ConfigureAwait(false))
                {
                    return;
                }

                continue;
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
            if (_singlePortClients.TryGetValue(client, out TftpTransfer? running)
                && running.Transport is SharedPortTftpTransport shared)
            {
                shared.Deliver(datagram.Span);
            }
            else if (opcode == TftpOpcode.WriteRequest)
            {
                PxeLog.TftpRefusedOperation(_logger, opcode, client);
                await SendErrorAsync(socket, localAddress, client, TftpErrorCode.IllegalOperation, "Write requests are not supported", cancellationToken)
                    .ConfigureAwait(false);
            }

            return;
        }

        if (!TftpPacket.TryReadReadRequest(datagram.Span, out TftpReadRequest? request)
            || !request.Mode.Equals("octet", StringComparison.OrdinalIgnoreCase))
        {
            PxeLog.TftpRefusedOperation(_logger, opcode, client);
            await SendErrorAsync(socket, localAddress, client, TftpErrorCode.IllegalOperation, "Only octet mode reads are supported", cancellationToken)
                .ConfigureAwait(false);

            return;
        }

        // A repeated request means the client has not yet seen an answer, or saw it late. With a port per
        // transfer the repeat simply gets its own transfer: the client keeps whichever port it heard from
        // first and the other times out unanswered. Cancelling the first could kill the one the client
        // chose. In single port mode both would share a port and interleave, so a repeat replaces a
        // transfer the client has not answered and is ignored once it has.
        if (_singlePortClients.TryGetValue(client, out TftpTransfer? previous))
        {
            if (previous.ClientAnswered)
            {
                return;
            }

            previous.Cancel();
        }

        // Dropped rather than refused: an ERROR aborts the client's boot, while a dropped request is
        // retransmitted and succeeds once a transfer finishes.
        if (_transfers.Count >= _maxTransfers)
        {
            PxeLog.TftpBusy(_logger, request.FileName, client, _maxTransfers);

            return;
        }

        if (!_files.TryResolve(request.FileName, out FileInfo? file))
        {
            PxeLog.TftpNotFound(_logger, request.FileName, client);
            await SendErrorAsync(socket, localAddress, client, TftpErrorCode.FileNotFound, "File not found", cancellationToken)
                .ConfigureAwait(false);

            return;
        }

        PxeLog.TftpRead(_logger, request.FileName, client, file.FullName);

        ITftpTransport transport = _singlePort
            ? new SharedPortTftpTransport(socket, client)
            : new ConnectedTftpTransport(PxeSocket.CreateTransfer(localAddress, client));

        TftpTransfer transfer = new(transport, client, file, request, _limits, _timeProvider, _logger, cancellationToken);

        _transfers[transfer] = client;

        if (_singlePort)
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

    private async Task SendErrorAsync(
        Socket socket,
        IPAddress localAddress,
        IPEndPoint client,
        TftpErrorCode code,
        string message,
        CancellationToken cancellationToken)
    {
        byte[] packet = new byte[TftpPacket.HeaderLength + message.Length + 1];

        if (!TftpPacket.TryWriteError(packet, code, message, out int length))
        {
            return;
        }

        using ITftpTransport transport = _singlePort
            ? new SharedPortTftpTransport(socket, client)
            : new ConnectedTftpTransport(PxeSocket.CreateTransfer(localAddress, client));

        try
        {
            await transport.SendAsync(packet.AsMemory(0, length), cancellationToken).ConfigureAwait(false);
        }
        catch (SocketException exception) when (PxeSocket.IsTransient(exception))
        {
        }
    }
}
