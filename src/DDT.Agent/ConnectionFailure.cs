// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Net.Sockets;
using System.Text.Json;
using DDT.ConsoleProtocol;

namespace DDT.Agent;

// Why the agent could not connect to the server, in words for an administrator rather than a socket error's, with the
// address it tried, as a mistyped one is the likeliest cause. HttpAgentServer throws them as the failure's message.
public static class ConnectionFailure
{
    // Null for a failure these words do not cover, whose own message stands.
    public static string? Describe(HttpRequestException exception, Uri server)
    {
        ArgumentNullException.ThrowIfNull(exception);
        ArgumentNullException.ThrowIfNull(server);

        return exception switch
        {
            { HttpRequestError: HttpRequestError.NameResolutionError } => $"the name {server.Host} cannot be found in DNS",
            { HttpRequestError: HttpRequestError.ConnectionError, InnerException: SocketException socket } => socket.SocketErrorCode switch
            {
                SocketError.ConnectionRefused => $"the connection to {server.Host}:{server.Port} was refused, so nothing listens on that port",
                SocketError.NetworkUnreachable or SocketError.NetworkDown or SocketError.HostUnreachable or SocketError.HostDown =>
                    $"this machine has no network route to {server.Host}",
                _ => null,
            },
            _ => null,
        };
    }

    // How far a failed call to the server got, for the console at the machine, or null for a failure that says nothing
    // about the connection.
    public static ConnectionStage? StageOf(Exception exception) => exception switch
    {
        ServerTimeoutException timeout => timeout.Stage,
        HttpRequestException { HttpRequestError: HttpRequestError.NameResolutionError } => ConnectionStage.NameLookup,
        HttpRequestException { HttpRequestError: HttpRequestError.ConnectionError } => ConnectionStage.Connection,
        HttpRequestException { HttpRequestError: HttpRequestError.SecureConnectionError } => ConnectionStage.SecureConnection,
        HttpRequestException or TimeoutException or JsonException or AgentTokenRejectedException => ConnectionStage.Answer,
        _ => null,
    };
}
