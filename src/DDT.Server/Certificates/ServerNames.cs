// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;

namespace DDT.Server.Certificates;

// The agent validates the host name against the certificate, so every name the server is reached by has to be in it.
public static class ServerNames
{
    // Returns localhost, this computer's name, its DNS name, and the names in the comma separated
    // DDT:Https:SubjectAlternativeNames.
    public static IReadOnlyList<string> Required(string configured)
    {
        ArgumentNullException.ThrowIfNull(configured);

        List<string> names = ["localhost", Dns.GetHostName()];

        foreach (string name in configured.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Prepend(DnsName()))
        {
            if (!names.Contains(name, StringComparer.OrdinalIgnoreCase))
            {
                names.Add(name);
            }
        }

        return names;
    }

    // deploy01.contoso.local, which the installers print. Windows PE resolves a short name only with the right DNS
    // suffix from DHCP. The computer's own name where it has no domain, or none that makes a host name: Linux reports
    // "(none)".
    public static string DnsName() => DnsName(Dns.GetHostName(), IPGlobalProperties.GetIPGlobalProperties().DomainName);

    public static string DnsName(string host, string domain)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(domain);

        string name = $"{host}.{domain.Trim('.')}";

        return domain.Length > 0 && !host.Contains('.', StringComparison.Ordinal) && Uri.CheckHostName(name) == UriHostNameType.Dns ? name : host;
    }

    // These addresses go into a certificate when one is issued, but they're never a reason to issue one. An IPv6
    // privacy address changes daily. Loopback is for a browser or a proxy on the server itself.
    public static IReadOnlyList<IPAddress> LocalAddresses()
    {
        IPAddress[] loopback = [IPAddress.Loopback, IPAddress.IPv6Loopback];

        try
        {
            return
            [
                .. loopback,
                .. Dns.GetHostAddresses(Dns.GetHostName())
                    .Where(a => a.AddressFamily is AddressFamily.InterNetwork or AddressFamily.InterNetworkV6)
                    .Except(loopback),
            ];
        }
        catch (SocketException)
        {
            return loopback;
        }
    }

    // Returns the DNS names and addresses in the certificate's subject alternative names.
    public static IReadOnlyList<string> Of(X509Certificate2 certificate)
    {
        ArgumentNullException.ThrowIfNull(certificate);

        X509SubjectAlternativeNameExtension? names = certificate.Extensions.OfType<X509SubjectAlternativeNameExtension>().FirstOrDefault();

        return names is null
            ? []
            : [.. names.EnumerateDnsNames(), .. names.EnumerateIPAddresses().Select(address => address.ToString())];
    }

    // True when every name appears in the certificate's subject alternative names. Addresses are compared as addresses,
    // not as text.
    public static bool Covers(X509Certificate2 certificate, IEnumerable<string> names)
    {
        ArgumentNullException.ThrowIfNull(names);

        IReadOnlyList<string> present = Of(certificate);

        return names.All(name => IPAddress.TryParse(name, out IPAddress? address)
            ? present.Any(p => IPAddress.TryParse(p, out IPAddress? other) && other.Equals(address))
            : present.Contains(name, StringComparer.OrdinalIgnoreCase));
    }
}
