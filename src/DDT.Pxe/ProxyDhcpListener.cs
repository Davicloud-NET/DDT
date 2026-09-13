using System.Net;
using System.Net.Sockets;
using DDT.Protocols.Pxe;
using Microsoft.Extensions.Logging;

namespace DDT.Pxe;

public sealed class ProxyDhcpListener : IAsyncDisposable
{
    // RFC 2131 only requires 576 octets, but nothing stops a client sending a jumbo datagram. A larger
    // one is reported as MessageSize and skipped rather than truncated into something parseable.
    private const int ReceiveBufferLength = 4096;
    private const int ReplyBufferLength = 1500;

    private readonly ProxyDhcpListenPort _role;
    private readonly IPEndPoint _bindTo;
    private readonly ProxyDhcpHandler _handler;
    private readonly NetworkInterfaceMap _interfaces;
    private readonly ILogger _logger;
    private readonly CancellationTokenSource _stopping = new();

    private Socket? _socket;
    private Task _loop = Task.CompletedTask;

    public ProxyDhcpListener(
        ProxyDhcpListenPort role,
        IPEndPoint bindTo,
        ProxyDhcpHandler handler,
        NetworkInterfaceMap interfaces,
        ILogger<ProxyDhcpListener> logger)
    {
        ArgumentNullException.ThrowIfNull(bindTo);
        ArgumentNullException.ThrowIfNull(handler);
        ArgumentNullException.ThrowIfNull(interfaces);
        ArgumentNullException.ThrowIfNull(logger);

        _role = role;
        _bindTo = bindTo;
        _handler = handler;
        _interfaces = interfaces;
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
        _socket?.Dispose();
        await _loop.ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
        _stopping.Dispose();
    }

    private async Task RunAsync(Socket socket, CancellationToken cancellationToken)
    {
        byte[] buffer = new byte[ReceiveBufferLength];
        byte[] reply = new byte[ReplyBufferLength];
        EndPoint anySource = new IPEndPoint(IPAddress.Any, 0);
        int port = ((IPEndPoint)socket.LocalEndPoint!).Port;
        int egressInterface = 0;
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
                PxeLog.ReceiveFailed(_logger, port, exception);

                if (!await PxeSocket.PauseAsync(cancellationToken).ConfigureAwait(false))
                {
                    return;
                }

                continue;
            }

            IPEndPoint source = (IPEndPoint)received.RemoteEndPoint;

            // Nothing a client sends may end this loop: a listener that dies on one malformed datagram
            // leaves every machine on the segment unable to boot until the process restarts.
            try
            {
                ProxyDhcpOutcome outcome = _handler.Handle(
                    buffer.AsSpan(0, received.ReceivedBytes),
                    _role,
                    received.PacketInformation.Interface,
                    received.PacketInformation.Address,
                    source,
                    reply);

                if (!seenFirst && outcome.Kind != ProxyDhcpOutcomeKind.InterfaceNotServed
                    && _interfaces.TryGetInterface(received.PacketInformation.Interface, out ServedInterface? served))
                {
                    seenFirst = true;
                    PxeLog.FirstDatagram(_logger, port, source, served.Name);
                }

                if (outcome.Kind == ProxyDhcpOutcomeKind.Replied)
                {
                    // IP_UNICAST_IF is per socket state, which is safe only because this loop sends one
                    // reply at a time.
                    if (outcome.EgressInterface != egressInterface)
                    {
                        PxeSocket.SetEgressInterface(socket, outcome.EgressInterface);
                        egressInterface = outcome.EgressInterface;
                    }

                    await SendAsync(socket, reply.AsMemory(0, outcome.Length), outcome, port, cancellationToken)
                        .ConfigureAwait(false);
                }
                else
                {
                    LogUnanswered(outcome, source, received.PacketInformation.Interface, port);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                PxeLog.DatagramFailed(_logger, port, exception);
            }
        }
    }

    private async Task SendAsync(
        Socket socket,
        ReadOnlyMemory<byte> datagram,
        ProxyDhcpOutcome outcome,
        int port,
        CancellationToken cancellationToken)
    {
        ProxyDhcpReply reply = outcome.Reply!;
        IPEndPoint destination = new(reply.Destination.Address, reply.Destination.Port);

        try
        {
            await socket.SendToAsync(datagram, SocketFlags.None, destination, cancellationToken).ConfigureAwait(false);
        }
        catch (SocketException exception)
        {
            PxeLog.SendFailed(_logger, destination.ToString(), port, exception);

            return;
        }

        PxeLog.Replied(
            _logger,
            reply.MessageType,
            reply.BootFileName,
            FormatMac(outcome),
            outcome.Message?.Architecture,
            port,
            destination.ToString());
    }

    private void LogUnanswered(ProxyDhcpOutcome outcome, IPEndPoint source, int arrivalInterface, int port)
    {
        switch (outcome.Kind)
        {
            case ProxyDhcpOutcomeKind.InterfaceNotServed:
                PxeLog.InterfaceNotServed(_logger, source, port, arrivalInterface);
                break;
            case ProxyDhcpOutcomeKind.Unparseable:
                PxeLog.Unparseable(_logger, source, port, outcome.ParseError);
                break;
            case ProxyDhcpOutcomeKind.ReplyTooLarge:
                PxeLog.ReplyTooLarge(_logger, FormatMac(outcome), port);
                break;
            case ProxyDhcpOutcomeKind.Silenced when IsRefusedPxeClient(outcome.SilenceReason):
                PxeLog.RefusedPxeClient(_logger, FormatMac(outcome), outcome.Message?.Architecture, port, outcome.SilenceReason);
                break;
            case ProxyDhcpOutcomeKind.Silenced:
                PxeLog.Ignored(_logger, FormatMac(outcome), source, port, outcome.SilenceReason);
                break;
            default:
                break;
        }
    }

    // Every ordinary workstation on the segment ends at the vendor class checks, so those stay at
    // Debug. The rest are PXE clients that DDT turned away.
    private static bool IsRefusedPxeClient(ProxyDhcpSilenceReason reason) =>
        reason is ProxyDhcpSilenceReason.NoClientArchitecture
            or ProxyDhcpSilenceReason.MalformedClientArchitecture
            or ProxyDhcpSilenceReason.NoBootTargetForArchitecture
            or ProxyDhcpSilenceReason.BootMethodDoesNotMatchVendorClass
            or ProxyDhcpSilenceReason.RelayNotAuthorised;

    private static string FormatMac(ProxyDhcpOutcome outcome) =>
        outcome.Message is { } message ? Convert.ToHexString(message.ClientHardwareAddress.Span) : string.Empty;
}
