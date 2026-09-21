// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;

namespace DDT.Server.Certificates;

// The agent validates the host name against the certificate, so every name the server is reached by has to be in it.
public static class ServerNames
{
    // localhost, this computer's name and the configured names, from the comma separated DDT:Https:SubjectAlternativeNames.
    public static IReadOnlyList<string> Required(string configured)
    {
        ArgumentNullException.ThrowIfNull(configured);

        List<string> names = ["localhost", Dns.GetHostName()];

        foreach (string name in configured.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!names.Contains(name, StringComparer.OrdinalIgnoreCase))
            {
                names.Add(name);
            }
        }

        return names;
    }

    // Added when a certificate is issued, but never a reason to issue one: an IPv6 privacy address changes daily.
    public static IReadOnlyList<IPAddress> LocalAddresses()
    {
        try
        {
            return
            [
                .. Dns.GetHostAddresses(Dns.GetHostName())
                    .Where(a => a.AddressFamily is AddressFamily.InterNetwork or AddressFamily.InterNetworkV6),
            ];
        }
        catch (SocketException)
        {
            return [];
        }
    }

    // The DNS names and addresses in the certificate's subject alternative names.
    public static IReadOnlyList<string> Of(X509Certificate2 certificate)
    {
        ArgumentNullException.ThrowIfNull(certificate);

        X509SubjectAlternativeNameExtension? names = certificate.Extensions.OfType<X509SubjectAlternativeNameExtension>().FirstOrDefault();

        return names is null
            ? []
            : [.. names.EnumerateDnsNames(), .. names.EnumerateIPAddresses().Select(address => address.ToString())];
    }
}
