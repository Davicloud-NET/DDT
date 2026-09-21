// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Server.Certificates;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging;

namespace DDT.Server.Tests;

// A store that still holds the self-signed certificate DDT generated before it had a root, as on the first start after
// an upgrade.
public sealed class LegacyCertificateApplication : DdtApplication
{
    public LegacyCertificateApplication()
    {
        Files = new CertificateFiles(Path.Combine(StorePath, "certs", "ddt.pem"), Path.Combine(StorePath, "certs", "ddt-key.pem"));
        Legacy = LegacyCertificate.Create("localhost", "ddt.lab.example");
        CertificateFolder.Write(Files, Legacy);
    }

    public CertificateFiles Files { get; }

    public PemPair Legacy { get; }

    // What the host logged, from its start on.
    public RecordingLoggerProvider Log { get; } = new();

    protected override void ConfigureTestHost(IWebHostBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.UseSetting("Kestrel:Certificates:Default:Path", Files.CertificatePath);
        builder.UseSetting("Kestrel:Certificates:Default:KeyPath", Files.KeyPath);
        builder.ConfigureLogging(logging => logging.AddProvider(Log));
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        if (disposing)
        {
            Log.Dispose();
        }
    }
}
