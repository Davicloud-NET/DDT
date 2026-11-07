// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using DDT.Contracts.Agents;

namespace DDT.Agent;

// Trusts exactly the root the boot image pins, and nothing in the machine store. It remembers how the latest
// handshake went, so a refused connection can say what to fix.
internal sealed class PinnedRootTls(X509Certificate2 root)
{
    // Where the agent runs, which decides how a root gets to it and so what to fix.
    public AgentEnvironment Environment { get; set; } = AgentEnvironment.WindowsPE;

    // How DDT names its roots, so a refused certificate from a DDT root can be told apart from an administrator's.
    private const string DdtRootSubjectPrefix = "CN=DDT root ";

    private int _certificateErrors;
    private string? _certificateIssuer;

    // Revocation isn't checked and missing intermediates aren't downloaded. A provisioning network has no route to
    // either, and each attempt stalls the handshake past the connect timeout. The server has to send its full chain.
    public void Apply(SslClientAuthenticationOptions options)
    {
        options.CertificateChainPolicy = new X509ChainPolicy
        {
            TrustMode = X509ChainTrustMode.CustomRootTrust,
            RevocationMode = X509RevocationMode.NoCheck,
            DisableCertificateDownloads = true,
            CustomTrustStore = { root },
        };

        options.RemoteCertificateValidationCallback = (_, certificate, _, errors) =>
        {
            Volatile.Write(ref _certificateErrors, (int)errors);
            Volatile.Write(ref _certificateIssuer, certificate?.Issuer);

            return errors == SslPolicyErrors.None;
        };
    }

    // The console is all a technician at the machine sees, so a refused certificate names its fix.
    public string? Problem(Uri? requestUri)
    {
        SslPolicyErrors errors = (SslPolicyErrors)Volatile.Read(ref _certificateErrors);

        if (errors.HasFlag(SslPolicyErrors.RemoteCertificateChainErrors))
        {
            string? issuer = Volatile.Read(ref _certificateIssuer);

            if (issuer is not null && issuer.StartsWith(DdtRootSubjectPrefix, StringComparison.Ordinal))
            {
                return EnvironmentTexts.OtherDdtRoot(Environment);
            }

            return EnvironmentTexts.OtherIssuer(Environment, issuer);
        }

        if (errors.HasFlag(SslPolicyErrors.RemoteCertificateNameMismatch))
        {
            return $"The server's certificate does not name {requestUri?.Host}. Add that name to DDT:Https:SubjectAlternativeNames " +
                "on the server and restart it: it then issues a certificate with the name from the same root.";
        }

        return null;
    }
}
