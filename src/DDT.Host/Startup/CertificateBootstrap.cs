// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Server.Certificates;
using DDT.Server.Configuration;

namespace DDT.Host.Startup;

public static class CertificateBootstrap
{
    // Null when the certificate is not two plain PEM files, which Kestrel then loads on its own as before.
    public static ServerCertificates? Create(IConfiguration configuration, DdtOptions options)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(options);

        return CertificateFiles.FromConfiguration(configuration) is { } files
            ? new ServerCertificates(
                files,
                ServerNames.Required(options.Https.SubjectAlternativeNames),
                options.Https.GenerateSelfSignedCertificate,
                TimeProvider.System)
            : null;
    }
}
