// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Xunit;

namespace DDT.Agent.Tests;

// The agent trusts the root in agent.json and nothing else, over a real TLS handshake.
public sealed class PinnedRootTests
{
    private const string Address = "127.0.0.1";

    private static readonly TimeSpan s_requestTimeout = TimeSpan.FromSeconds(30);

    [Fact]
    public async Task ACertificateFromThePinnedRootIsAcceptedAndSoIsItsRenewal()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        using X509Certificate2 root = TestCertificates.Root("DDT root 0001");
        using X509Certificate2 pin = TestCertificates.Pin(root);
        using X509Certificate2 first = TestCertificates.Issue(root, Address);
        using X509Certificate2 renewed = TestCertificates.Issue(root, Address);

        foreach (X509Certificate2 certificate in new[] { first, renewed })
        {
            using TcpListener listener = new(IPAddress.Loopback, 0);
            listener.Start();
            Task serving = AnswerNotFoundAsync(listener, certificate, cancellationToken);
            using HttpAgentServer server = new(AddressOf(listener), pin, s_requestTimeout);

            Assert.Null(await server.GetReleaseAsync(cancellationToken));
            await serving;
        }
    }

    [Fact]
    public async Task ACertificateFromAnotherRootIsRefusedWithTheFix()
    {
        using X509Certificate2 pinnedRoot = TestCertificates.Root("DDT root 0001");
        using X509Certificate2 pin = TestCertificates.Pin(pinnedRoot);
        using X509Certificate2 otherRoot = TestCertificates.Root("DDT root 0002");
        using X509Certificate2 certificate = TestCertificates.Issue(otherRoot, Address);

        HttpRequestException refusal = await RefusedAsync(pin, certificate);

        Assert.Contains("Build-BootImage.ps1 -RootCertificatePath", refusal.Message, StringComparison.Ordinal);
        Assert.Contains("ddt-root.pem", refusal.Message, StringComparison.Ordinal);
    }

    // DDT's root isn't the fix for a certificate an administrator brought. Its CA's root is, or the intermediate the
    // server left out.
    [Fact]
    public async Task ACertificateFromAnotherCaIsRefusedWithoutPointingAtDdtsRoot()
    {
        using X509Certificate2 pinnedRoot = TestCertificates.Root("DDT root 0001");
        using X509Certificate2 pin = TestCertificates.Pin(pinnedRoot);
        using X509Certificate2 otherCa = TestCertificates.Root("Example Issuing CA");
        using X509Certificate2 certificate = TestCertificates.Issue(otherCa, Address);

        HttpRequestException refusal = await RefusedAsync(pin, certificate);

        Assert.Contains("issued by CN=Example Issuing CA", refusal.Message, StringComparison.Ordinal);
        Assert.Contains("intermediate", refusal.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("ddt-root.pem", refusal.Message, StringComparison.Ordinal);
    }

    // A boot image built before the server had a root pins the self-signed certificate the server used then.
    [Fact]
    public async Task ABootImageThatPinsTheOldSelfSignedCertificateIsToldToBeBuiltAgain()
    {
        using X509Certificate2 old = TestCertificates.SelfSigned(Address);
        using X509Certificate2 pin = TestCertificates.Pin(old);
        using X509Certificate2 root = TestCertificates.Root("DDT root 0001");
        using X509Certificate2 certificate = TestCertificates.Issue(root, Address);

        HttpRequestException refusal = await RefusedAsync(pin, certificate);

        Assert.Equal(HttpRequestError.SecureConnectionError, refusal.HttpRequestError);
        Assert.Contains("Build-BootImage.ps1 -RootCertificatePath", refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ACertificateWithoutTheServersNameSaysWhichNameIsMissing()
    {
        using X509Certificate2 root = TestCertificates.Root("DDT root 0001");
        using X509Certificate2 pin = TestCertificates.Pin(root);
        using X509Certificate2 certificate = TestCertificates.Issue(root, "ddt.example");

        HttpRequestException refusal = await RefusedAsync(pin, certificate);

        Assert.Contains($"does not name {Address}", refusal.Message, StringComparison.Ordinal);
        Assert.Contains("DDT:Https:SubjectAlternativeNames", refusal.Message, StringComparison.Ordinal);
    }

    private static async Task<HttpRequestException> RefusedAsync(X509Certificate2 pin, X509Certificate2 certificate)
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        using TcpListener listener = new(IPAddress.Loopback, 0);
        listener.Start();
        Task serving = AnswerNotFoundAsync(listener, certificate, cancellationToken);
        using HttpAgentServer server = new(AddressOf(listener), pin, s_requestTimeout);

        HttpRequestException refusal = await Assert.ThrowsAsync<HttpRequestException>(() => server.GetReleaseAsync(cancellationToken));
        await serving;

        return refusal;
    }

    private static Uri AddressOf(TcpListener listener) =>
        new($"https://{Address}:{((IPEndPoint)listener.LocalEndpoint).Port}/");

    // Answers one request with 404, which the agent reads as a server that doesn't offer an agent.
    private static async Task AnswerNotFoundAsync(TcpListener listener, X509Certificate2 certificate, CancellationToken cancellationToken)
    {
        using TcpClient client = await listener.AcceptTcpClientAsync(cancellationToken);
        await using SslStream tls = new(client.GetStream());

        try
        {
            await tls.AuthenticateAsServerAsync(
                new SslServerAuthenticationOptions { ServerCertificate = certificate },
                cancellationToken);

            byte[] buffer = new byte[8192];
            int read = 0;

            while (!Encoding.ASCII.GetString(buffer, 0, read).Contains("\r\n\r\n", StringComparison.Ordinal))
            {
                int received = await tls.ReadAsync(buffer.AsMemory(read), cancellationToken);
                Assert.NotEqual(0, received);
                read += received;
            }

            await tls.WriteAsync("HTTP/1.1 404 Not Found\r\nContent-Length: 0\r\nConnection: close\r\n\r\n"u8.ToArray(), cancellationToken);
        }
        catch (Exception exception) when (exception is AuthenticationException or IOException)
        {
            // The agent refused the certificate and dropped the connection.
        }
    }
}
