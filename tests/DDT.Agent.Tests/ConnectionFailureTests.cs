// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using DDT.ConsoleProtocol;
using Xunit;

namespace DDT.Agent.Tests;

public sealed class ConnectionFailureTests
{
    private static readonly Uri s_server = new("https://ddt.example:8443/");

    // How SocketsHttpHandler reports a connection that failed: the socket's text with the address, and the socket error
    // inside.
    private static HttpRequestException Failed(HttpRequestError error, SocketError socketError) =>
        new(error, $"{new SocketException((int)socketError).Message} (ddt.example:8443)", new SocketException((int)socketError));

    [Fact]
    public void SaysThatNothingListensOnTheServersPort() =>
        Assert.Equal(
            "the connection to ddt.example:8443 was refused, so nothing listens on that port",
            ConnectionFailure.Describe(Failed(HttpRequestError.ConnectionError, SocketError.ConnectionRefused), s_server));

    // The address the agent was given may leave the port out.
    [Fact]
    public void NamesTheDefaultPortToo() =>
        Assert.Equal(
            "the connection to ddt.example:443 was refused, so nothing listens on that port",
            ConnectionFailure.Describe(Failed(HttpRequestError.ConnectionError, SocketError.ConnectionRefused), new Uri("https://ddt.example/")));

    [Theory]
    [InlineData(SocketError.NetworkUnreachable)]
    [InlineData(SocketError.NetworkDown)]
    [InlineData(SocketError.HostUnreachable)]
    [InlineData(SocketError.HostDown)]
    public void SaysThatAnUnreachableServerHasNoRoute(SocketError socketError) =>
        Assert.Equal(
            "this machine has no network route to ddt.example",
            ConnectionFailure.Describe(Failed(HttpRequestError.ConnectionError, socketError), s_server));

    [Theory]
    [InlineData(SocketError.HostNotFound)]
    [InlineData(SocketError.TryAgain)]
    public void SaysThatTheServersNameIsNotInDns(SocketError socketError) =>
        Assert.Equal(
            "the name ddt.example cannot be found in DNS",
            ConnectionFailure.Describe(Failed(HttpRequestError.NameResolutionError, socketError), s_server));

    // Their own messages stand: HttpAgentServer's fix for a refused certificate, for example.
    [Fact]
    public void HasNoWordsForAnyOtherFailure()
    {
        Assert.Null(ConnectionFailure.Describe(Failed(HttpRequestError.ConnectionError, SocketError.ConnectionReset), s_server));
        Assert.Null(ConnectionFailure.Describe(new HttpRequestException(HttpRequestError.SecureConnectionError, "The server's certificate does not name ddt.example."), s_server));
        Assert.Null(ConnectionFailure.Describe(new HttpRequestException("The server sent an empty answer."), s_server));
    }

    // For the console at the machine, which shows how far a call got.
    [Fact]
    public void TellsHowFarAFailedCallGot()
    {
        Assert.Equal(ConnectionStage.NameLookup, ConnectionFailure.StageOf(Failed(HttpRequestError.NameResolutionError, SocketError.HostNotFound)));
        Assert.Equal(ConnectionStage.Connection, ConnectionFailure.StageOf(Failed(HttpRequestError.ConnectionError, SocketError.ConnectionRefused)));
        Assert.Equal(
            ConnectionStage.SecureConnection,
            ConnectionFailure.StageOf(new HttpRequestException(HttpRequestError.SecureConnectionError, "The server's certificate does not name ddt.example.")));
        Assert.Equal(
            ConnectionStage.Connection,
            ConnectionFailure.StageOf(new ServerTimeoutException(ConnectionStage.Connection, "did not accept a connection", new TimeoutException())));
        Assert.Equal(ConnectionStage.Answer, ConnectionFailure.StageOf(new AgentRequestException("503", null, HttpStatusCode.ServiceUnavailable)));
        Assert.Equal(ConnectionStage.Answer, ConnectionFailure.StageOf(new JsonException("An HTML page.")));
        Assert.Null(ConnectionFailure.StageOf(new InvalidOperationException()));
    }
}
