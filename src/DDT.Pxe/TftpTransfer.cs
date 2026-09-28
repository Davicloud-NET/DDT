// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Net;
using System.Net.Sockets;
using DDT.Protocols.Tftp;
using Microsoft.Extensions.Logging;
using Microsoft.Win32.SafeHandles;

namespace DDT.Pxe;

// Drives one TftpReadSession over its transport. The receive and the retransmit timeout
// happen in the same loop, so the session is only ever touched by one thread and needs no lock.
internal sealed class TftpTransfer
{
    // An acknowledgement is four octets and an error is short; anything larger is not from a client.
    private const int ReceiveBufferLength = TftpPacket.HeaderLength + 512;

    private readonly ITftpTransport _transport;
    private readonly IPEndPoint _client;
    private readonly FileInfo _file;
    private readonly TftpReadRequest _request;
    private readonly TftpLimits _limits;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger _logger;
    private readonly CancellationToken _stopping;
    private readonly CancellationTokenSource _cancellation;

    private volatile bool _clientAnswered;

    public TftpTransfer(
        ITftpTransport transport,
        IPEndPoint client,
        FileInfo file,
        TftpReadRequest request,
        TftpTransferServices services,
        CancellationToken stopping)
    {
        _transport = transport;
        _client = client;
        _file = file;
        _request = request;
        _limits = services.Limits;
        _timeProvider = services.TimeProvider;
        _logger = services.Logger;
        _stopping = stopping;
        _cancellation = CancellationTokenSource.CreateLinkedTokenSource(stopping);
    }

    public ITftpTransport Transport => _transport;

    // Set once the client has acknowledged something, which means it has settled on this transfer.
    public bool ClientAnswered => _clientAnswered;

    public Task Completion { get; private set; } = Task.CompletedTask;

    public Task Start()
    {
        Completion = RunAsync();

        return Completion;
    }

    public void Cancel()
    {
        try
        {
            _cancellation.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // The transfer finished between being looked up and being cancelled.
        }
    }

    private async Task RunAsync()
    {
        CancellationToken cancellationToken = _cancellation.Token;
        long started = _timeProvider.GetTimestamp();

        try
        {
            using SafeFileHandle handle = File.OpenHandle(_file.FullName, FileMode.Open, FileAccess.Read, FileShare.Read);

            long fileLength = RandomAccess.GetLength(handle);
            TftpReadSession session = new(_request, fileLength, _limits, _timeProvider);

            // Sized for the largest datagram the session can ask for: a data block, or an OACK that is
            // bigger than a block when the client negotiated a tiny block size.
            byte[] send = new byte[TftpPacket.HeaderLength + Math.Max(512, session.Negotiated.BlockSize)];
            byte[] receive = new byte[ReceiveBufferLength];
            DateTimeOffset? deadline = null;
            TftpStep step = session.Start();

            while (true)
            {
                deadline = await ApplyAsync(step, handle, send, deadline, cancellationToken).ConfigureAwait(false);

                if (step.State is TftpSessionState.Completed or TftpSessionState.Failed)
                {
                    break;
                }

                step = await WaitAsync(session, deadline, receive, cancellationToken).ConfigureAwait(false);
            }

            LogOutcome(session, fileLength, _timeProvider.GetElapsedTime(started));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // A replaced transfer goes quietly because the client never answered it. On shutdown the
            // client is told, so it fails now instead of after its own timeouts.
            if (_stopping.IsCancellationRequested)
            {
                await TrySendErrorAsync(TftpErrorCode.NotDefined, "Server shutting down").ConfigureAwait(false);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or SocketException or ObjectDisposedException)
        {
            PxeLog.TftpError(_logger, _file.FullName, _client, exception);
            await TrySendErrorAsync(TftpErrorCode.NotDefined, "Read failed").ConfigureAwait(false);
        }
        finally
        {
            _transport.Dispose();
            _cancellation.Dispose();
        }
    }

    private async Task<DateTimeOffset?> ApplyAsync(
        TftpStep step,
        SafeFileHandle handle,
        byte[] send,
        DateTimeOffset? deadline,
        CancellationToken cancellationToken)
    {
        foreach (TftpAction action in step.Actions)
        {
            switch (action)
            {
                case TftpSendOptionAck optionAck when TftpPacket.TryWriteOptionAck(send, optionAck.Negotiated, out int length):
                    await SendAsync(send.AsMemory(0, length), cancellationToken).ConfigureAwait(false);
                    break;

                case TftpSendData data:
                    TftpPacket.TryWriteDataHeader(send, data.Block);
                    await ReadExactAsync(handle, send.AsMemory(TftpPacket.HeaderLength, data.Length), data.FileOffset, cancellationToken)
                        .ConfigureAwait(false);
                    await SendAsync(send.AsMemory(0, TftpPacket.HeaderLength + data.Length), cancellationToken).ConfigureAwait(false);
                    break;

                case TftpSendError error when TftpPacket.TryWriteError(send, error.Code, error.Message, out int length):
                    await SendAsync(send.AsMemory(0, length), cancellationToken).ConfigureAwait(false);
                    break;

                case TftpArmRetransmit arm:
                    deadline = arm.Deadline;
                    break;

                case TftpStopRetransmit:
                    deadline = null;
                    break;

                default:
                    break;
            }
        }

        return deadline;
    }

    private async Task<TftpStep> WaitAsync(
        TftpReadSession session,
        DateTimeOffset? deadline,
        byte[] receive,
        CancellationToken cancellationToken)
    {
        TimeSpan remaining = (deadline ?? _timeProvider.GetUtcNow() + _limits.MaxRetransmitDelay) - _timeProvider.GetUtcNow();

        if (remaining <= TimeSpan.Zero)
        {
            return session.OnRetransmitTimeout();
        }

        using CancellationTokenSource timeout = new(remaining, _timeProvider);
        using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);

        try
        {
            int received = await _transport.ReceiveAsync(receive, linked.Token).ConfigureAwait(false);

            TftpStep step = session.OnDatagram(receive.AsSpan(0, received));

            // A duplicate or stray acknowledgement produces no actions, so only a real one counts.
            if (step.Actions.Count > 0)
            {
                _clientAnswered = true;
            }

            return step;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return session.OnRetransmitTimeout();
        }
        catch (SocketException exception) when (PxeSocket.IsTransient(exception))
        {
            // The deadline is absolute, so waiting again resumes the same timeout rather than restarting it.
            return TftpStep.Nothing(session.State);
        }
    }

    private async Task SendAsync(ReadOnlyMemory<byte> datagram, CancellationToken cancellationToken)
    {
        try
        {
            await _transport.SendAsync(datagram, cancellationToken).ConfigureAwait(false);
        }
        catch (SocketException exception) when (PxeSocket.IsTransient(exception))
        {
            // The client is unreachable for now. The retransmit timer decides when it is gone.
        }
    }

    // RandomAccess may return fewer bytes than asked for, and a short DATA block is the protocol's end
    // of transfer signal, so one short read would silently truncate boot.wim.
    private static async Task ReadExactAsync(
        SafeFileHandle handle,
        Memory<byte> destination,
        long offset,
        CancellationToken cancellationToken)
    {
        int total = 0;

        while (total < destination.Length)
        {
            int read = await RandomAccess.ReadAsync(handle, destination[total..], offset + total, cancellationToken)
                .ConfigureAwait(false);

            if (read == 0)
            {
                throw new IOException("The boot file became shorter during the transfer.");
            }

            total += read;
        }
    }

    private async Task TrySendErrorAsync(TftpErrorCode code, string message)
    {
        byte[] packet = new byte[TftpPacket.HeaderLength + message.Length + 1];

        if (!TftpPacket.TryWriteError(packet, code, message, out int length))
        {
            return;
        }

        try
        {
            await _transport.SendAsync(packet.AsMemory(0, length), CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is SocketException or ObjectDisposedException)
        {
        }
    }

    private void LogOutcome(TftpReadSession session, long fileLength, TimeSpan elapsed)
    {
        switch (session.Failure)
        {
            case TftpFailure.None:
                PxeLog.TftpCompleted(
                    _logger,
                    _file.FullName,
                    _client,
                    fileLength,
                    session.Negotiated.BlockSize,
                    session.Negotiated.WindowSize,
                    (long)elapsed.TotalMilliseconds);
                break;
            case TftpFailure.ClientAborted:
                PxeLog.TftpAborted(_logger, _client, _file.FullName, session.AcknowledgedBlock);
                break;
            case TftpFailure.ClientGone when !_clientAnswered:
                PxeLog.TftpUnanswered(_logger, _client, _file.FullName);
                break;
            default:
                PxeLog.TftpFailed(_logger, _file.FullName, _client, session.Failure, session.AcknowledgedBlock);
                break;
        }
    }
}
