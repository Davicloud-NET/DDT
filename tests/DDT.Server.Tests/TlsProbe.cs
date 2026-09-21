// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using Xunit;

namespace DDT.Server.Tests;

// One pooled connection to a real Kestrel, counting its TLS handshakes and noting the certificate each one was served.
public sealed class TlsProbe : IDisposable
{
    private readonly HttpClient _client;
    private int _handshakes;
    private string? _serialNumber;

    public TlsProbe(Uri address)
    {
        SocketsHttpHandler handler = new()
        {
            MaxConnectionsPerServer = 1,
            PooledConnectionIdleTimeout = TimeSpan.FromMinutes(5),
        };

        // Which certificate came is the question here, not whether a client would trust it.
        handler.SslOptions.RemoteCertificateValidationCallback = (_, certificate, _, _) =>
        {
            Interlocked.Increment(ref _handshakes);
            Volatile.Write(ref _serialNumber, certificate?.GetSerialNumberString());

            return true;
        };

        _client = new HttpClient(handler) { BaseAddress = address };
    }

    public int Handshakes => Volatile.Read(ref _handshakes);

    // The serial number of the certificate the answering connection was set up with.
    public async Task<string> GetAsync(CancellationToken cancellationToken)
    {
        Assert.Equal("ok", await _client.GetStringAsync(new Uri("/", UriKind.Relative), cancellationToken));

        return Volatile.Read(ref _serialNumber) ?? throw new InvalidOperationException("No TLS handshake happened.");
    }

    public void Dispose() => _client.Dispose();
}
