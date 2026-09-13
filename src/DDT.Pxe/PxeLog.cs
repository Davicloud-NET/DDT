using System.Net;
using DDT.Protocols.Dhcp;
using DDT.Protocols.Pxe;
using DDT.Protocols.Tftp;
using Microsoft.Extensions.Logging;

namespace DDT.Pxe;

internal static partial class PxeLog
{
    [LoggerMessage(EventId = 500, Level = LogLevel.Warning, Message = "Answering PXE on interface {Name} ({Address}, index {Index})")]
    public static partial void ServingInterface(ILogger logger, string name, IPAddress address, int index);

    [LoggerMessage(
        EventId = 501,
        Level = LogLevel.Warning,
        Message = "The pxe role is active but DDT:Pxe:Interfaces names no interface on this host, so nothing is served. Candidates: {Candidates}")]
    public static partial void NoInterfaces(ILogger logger, string candidates);

    [LoggerMessage(EventId = 502, Level = LogLevel.Warning, Message = "DDT:Pxe:Interfaces names {Name}, which is not an active interface on this host")]
    public static partial void InterfaceNotFound(ILogger logger, string name);

    [LoggerMessage(EventId = 503, Level = LogLevel.Information, Message = "Boot target {Architecture}: {Method} {BootFile}")]
    public static partial void BootTarget(ILogger logger, ClientArchitecture architecture, BootMethod method, string bootFile);

    [LoggerMessage(EventId = 504, Level = LogLevel.Warning, Message = "Boot target {Architecture} names {BootFile}, which is not in {BootDirectory}")]
    public static partial void BootFileMissing(ILogger logger, ClientArchitecture architecture, string bootFile, string bootDirectory);

    [LoggerMessage(EventId = 505, Level = LogLevel.Warning, Message = "No boot targets are configured, so every PXE client is ignored")]
    public static partial void NoBootTargets(ILogger logger);

    [LoggerMessage(EventId = 506, Level = LogLevel.Information, Message = "{Protocol} listening on UDP {Port}")]
    public static partial void Listening(ILogger logger, string protocol, int port);

    [LoggerMessage(EventId = 507, Level = LogLevel.Information, Message = "HTTP boot listening on port {Port}, serving {BootDirectory}")]
    public static partial void HttpBootListening(ILogger logger, int port, string bootDirectory);

    // A successful bind proves nothing on Windows: another process holding a specific address on the
    // same port silently takes the unicast traffic. The first datagram is the real readiness signal.
    [LoggerMessage(EventId = 510, Level = LogLevel.Information, Message = "First datagram on UDP {Port}, from {Source} on interface {Name}")]
    public static partial void FirstDatagram(ILogger logger, int port, IPEndPoint source, string name);

    [LoggerMessage(
        EventId = 511,
        Level = LogLevel.Information,
        Message = "Sent {MessageType} for {BootFile} to {ClientMac} ({Architecture}) from UDP {Port} to {Destination}")]
    public static partial void Replied(
        ILogger logger,
        DhcpMessageType messageType,
        string bootFile,
        string clientMac,
        ClientArchitecture? architecture,
        int port,
        string destination);

    // These reasons are only reached after the vendor class matched, so each one is a real PXE client
    // being refused and has a configuration fix.
    [LoggerMessage(EventId = 512, Level = LogLevel.Information, Message = "Refused PXE client {ClientMac} ({Architecture}) on UDP {Port}: {Reason}")]
    public static partial void RefusedPxeClient(
        ILogger logger,
        string clientMac,
        ClientArchitecture? architecture,
        int port,
        ProxyDhcpSilenceReason reason);

    [LoggerMessage(EventId = 513, Level = LogLevel.Debug, Message = "Ignored {ClientMac} from {Source} on UDP {Port}: {Reason}")]
    public static partial void Ignored(ILogger logger, string clientMac, IPEndPoint source, int port, ProxyDhcpSilenceReason reason);

    [LoggerMessage(EventId = 514, Level = LogLevel.Debug, Message = "Ignored an unparseable datagram from {Source} on UDP {Port}: {Error}")]
    public static partial void Unparseable(ILogger logger, IPEndPoint source, int port, DhcpParseError error);

    [LoggerMessage(EventId = 515, Level = LogLevel.Trace, Message = "Ignored a datagram from {Source} on UDP {Port}: interface {Interface} is not served")]
    public static partial void InterfaceNotServed(ILogger logger, IPEndPoint source, int port, int @interface);

    [LoggerMessage(EventId = 516, Level = LogLevel.Warning, Message = "The reply to {ClientMac} on UDP {Port} does not fit in a datagram")]
    public static partial void ReplyTooLarge(ILogger logger, string clientMac, int port);

    [LoggerMessage(EventId = 517, Level = LogLevel.Warning, Message = "Could not send to {Destination} from UDP {Port}")]
    public static partial void SendFailed(ILogger logger, string destination, int port, Exception exception);

    [LoggerMessage(EventId = 518, Level = LogLevel.Error, Message = "Failed to handle a datagram on UDP {Port}")]
    public static partial void DatagramFailed(ILogger logger, int port, Exception exception);

    [LoggerMessage(EventId = 519, Level = LogLevel.Error, Message = "Receiving on UDP {Port} failed, retrying in a second")]
    public static partial void ReceiveFailed(ILogger logger, int port, Exception exception);

    [LoggerMessage(EventId = 600, Level = LogLevel.Information, Message = "TFTP read of {Requested} by {Client}, serving {Path}")]
    public static partial void TftpRead(ILogger logger, string requested, IPEndPoint client, string path);

    [LoggerMessage(EventId = 601, Level = LogLevel.Information, Message = "TFTP read of {Requested} by {Client} refused: no such file in the boot directory")]
    public static partial void TftpNotFound(ILogger logger, string requested, IPEndPoint client);

    [LoggerMessage(
        EventId = 602,
        Level = LogLevel.Information,
        Message = "Sent {Path} to {Client}: {Bytes} bytes, block size {BlockSize}, window {WindowSize}, {ElapsedMilliseconds} ms")]
    public static partial void TftpCompleted(
        ILogger logger,
        string path,
        IPEndPoint client,
        long bytes,
        int blockSize,
        int windowSize,
        long elapsedMilliseconds);

    [LoggerMessage(EventId = 603, Level = LogLevel.Warning, Message = "TFTP transfer of {Path} to {Client} ended: {Failure} after block {Block}")]
    public static partial void TftpFailed(ILogger logger, string path, IPEndPoint client, TftpFailure failure, long block);

    // Firmware routinely asks for a file with tsize only to learn its length and then aborts.
    [LoggerMessage(EventId = 604, Level = LogLevel.Debug, Message = "{Client} aborted the TFTP transfer of {Path} after block {Block}")]
    public static partial void TftpAborted(ILogger logger, IPEndPoint client, string path, long block);

    [LoggerMessage(
        EventId = 605,
        Level = LogLevel.Warning,
        Message = "Dropped a TFTP read of {Requested} by {Client}: {Limit} transfers are already running")]
    public static partial void TftpBusy(ILogger logger, string requested, IPEndPoint client, int limit);

    [LoggerMessage(EventId = 606, Level = LogLevel.Information, Message = "Refused a TFTP {Opcode} from {Client}")]
    public static partial void TftpRefusedOperation(ILogger logger, TftpOpcode opcode, IPEndPoint client);

    [LoggerMessage(EventId = 607, Level = LogLevel.Warning, Message = "TFTP transfer of {Path} to {Client} failed")]
    public static partial void TftpError(ILogger logger, string path, IPEndPoint client, Exception exception);

    // The usual cause is a repeated request: the client settled on the transfer that answered first.
    [LoggerMessage(EventId = 608, Level = LogLevel.Debug, Message = "{Client} never answered the TFTP transfer of {Path}")]
    public static partial void TftpUnanswered(ILogger logger, IPEndPoint client, string path);

    [LoggerMessage(EventId = 700, Level = LogLevel.Information, Message = "HTTP boot {Method} {Path} by {Client}")]
    public static partial void HttpBoot(ILogger logger, string method, string path, string? client);

    [LoggerMessage(EventId = 701, Level = LogLevel.Information, Message = "HTTP boot {Method} {Path} by {Client} refused: no such file in the boot directory")]
    public static partial void HttpBootNotFound(ILogger logger, string method, string path, string? client);
}
