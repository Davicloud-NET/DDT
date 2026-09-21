// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Server.Certificates;
using DDT.Server.Configuration;

namespace DDT.Host.Startup;

public static class CertificateBootstrap
{
    // Null when DDT does not look after the certificate: generation is off, or the paths are not two plain PEM files.
    public static ServerCertificates? Create(IConfiguration configuration, DdtOptions options)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(options);

        if (!options.Https.GenerateSelfSignedCertificate || CertificateFiles.FromConfiguration(configuration) is not { } files)
        {
            return null;
        }

        return new ServerCertificates(files, ServerNames.Required(options.Https.SubjectAlternativeNames));
    }
}
