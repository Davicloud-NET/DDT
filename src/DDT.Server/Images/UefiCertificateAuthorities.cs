// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Security.Cryptography.X509Certificates;

namespace DDT.Server.Images;

// Microsoft Corporation UEFI CA 2011 and Microsoft UEFI CA 2023, which sign what is not Windows in a stock PC's Secure
// Boot db, such as Linux shims. DDT.Server.csproj embeds them, as https://www.microsoft.com/pkiops/certs/ publishes
// them.
public static class UefiCertificateAuthorities
{
    public static IReadOnlyList<X509Certificate2> Microsoft { get; } =
    [
        Load("uefi/MicCorUEFCA2011_2011-06-27.crt"),
        Load("uefi/MicrosoftUEFICA2023.crt"),
    ];

    private static X509Certificate2 Load(string name)
    {
        using Stream resource = typeof(UefiCertificateAuthorities).Assembly.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException($"This server was built without {name}.");
        using MemoryStream bytes = new();
        resource.CopyTo(bytes);

        return X509CertificateLoader.LoadCertificate(bytes.ToArray());
    }
}
