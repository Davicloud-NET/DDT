// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Net.Security;
using System.Security.Cryptography.X509Certificates;

namespace DDT.Server.Certificates;

// The pair Kestrel serves, held in memory so a new pair reaches the next connection without a restart.
internal sealed class ServedCertificate
{
    private SslStreamCertificateContext? _context;
    private string? _rootPem;
    private string? _warnedThumbprint;

    // Read on every TLS handshake. The context it replaces is left to the garbage collector rather than disposed, because
    // a handshake that already picked it may still be using it.
    public SslStreamCertificateContext? Context => Volatile.Read(ref _context);

    // DDT's root while the served certificate comes from it, and null for an administrator's certificate.
    public string? RootPem => Volatile.Read(ref _rootPem);

    // The files as last loaded, or as they were when their load last failed.
    public FileStamp? Stamp { get; set; }

    public CertificateCheck Serve(SslStreamCertificateContext context, FileStamp stamp, string? rootPem, CertificateAction action, DateTimeOffset now)
    {
        X509Certificate2 certificate = context.TargetCertificate;
        Volatile.Write(ref _context, context);
        Volatile.Write(ref _rootPem, rootPem);
        Stamp = stamp;

        bool expiresSoon = rootPem is null
            && now >= CertificateChains.Utc(certificate.NotAfter) - ServerCertificates.RenewBefore
            && certificate.Thumbprint != _warnedThumbprint;

        if (expiresSoon)
        {
            _warnedThumbprint = certificate.Thumbprint;
        }

        return new CertificateCheck(action, certificate, rootPem is not null, expiresSoon, null);
    }
}
