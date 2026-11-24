// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using Xunit;

namespace DDT.Agent.Tests;

// A server of one connection that answers the agent's client from the test's script.
internal static class RawHttp
{
    public static string Json(byte[] body) =>
        $"HTTP/1.1 200 OK\r\nContent-Type: application/json\r\nContent-Length: {body.Length}\r\n";

    public static Uri AddressOf(TcpListener listener) =>
        new($"http://127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}/");

    // Answers one request right away with the given status line and headers, and returns the request with its body.
    public static async Task<string> AnswerAsync(TcpListener listener, string head, byte[] body, CancellationToken cancellationToken)
    {
        using TcpClient client = await listener.AcceptTcpClientAsync(cancellationToken);
        NetworkStream stream = client.GetStream();
        List<byte> request = [];
        byte[] buffer = new byte[8192];

        while (!IsComplete(Encoding.UTF8.GetString([.. request])))
        {
            int received = await stream.ReadAsync(buffer, cancellationToken);
            Assert.NotEqual(0, received);
            request.AddRange(buffer.AsSpan(0, received));
        }

        // In one write, so a client that stops after the headers cannot close the connection before the body is sent.
        await stream.WriteAsync((byte[])[.. Encoding.ASCII.GetBytes($"{head}Connection: close\r\n\r\n"), .. body], cancellationToken);

        return Encoding.UTF8.GetString([.. request]);
    }

    // A JSON body is sent in chunks, other bodies with a Content-Length.
    private static bool IsComplete(string request)
    {
        int headEnd = request.IndexOf("\r\n\r\n", StringComparison.Ordinal);

        if (headEnd < 0)
        {
            return false;
        }

        string head = request[..headEnd];
        string body = request[(headEnd + 4)..];

        if (head.Contains("Transfer-Encoding: chunked", StringComparison.OrdinalIgnoreCase))
        {
            return body.EndsWith("0\r\n\r\n", StringComparison.Ordinal);
        }

        Match length = Regex.Match(head, @"Content-Length: (\d+)", RegexOptions.IgnoreCase);

        return !length.Success || Encoding.UTF8.GetByteCount(body) >= int.Parse(length.Groups[1].Value, CultureInfo.InvariantCulture);
    }
}
