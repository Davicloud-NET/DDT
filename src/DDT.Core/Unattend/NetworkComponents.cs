// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Xml;
using static DDT.Core.Unattend.UnattendXml;

namespace DDT.Core.Unattend;

// Microsoft-Windows-TCPIP and Microsoft-Windows-DNS-Client for one adapter, in specialize. Setup wants an Interface's
// children in schema order: Ipv4Settings, Identifier, UnicastIpAddresses, Routes; a Route's: Identifier, NextHopAddress,
// Prefix.
internal static class NetworkComponents
{
    private const string DefaultRoute = "0.0.0.0/0";

    public static void Write(XmlWriter writer, StaticNetwork network, string processorArchitecture)
    {
        StartComponent(writer, "Microsoft-Windows-TCPIP", processorArchitecture);
        StartElement(writer, "Interfaces");
        StartAdded(writer, "Interface");
        StartElement(writer, "Ipv4Settings");
        WriteElement(writer, "DhcpEnabled", Flag(false));
        writer.WriteEndElement();
        WriteElement(writer, "Identifier", network.Identifier);
        StartElement(writer, "UnicastIpAddresses");
        WriteKeyed(writer, "IpAddress", network.Addresses);
        writer.WriteEndElement();

        if (network.Gateway is { } gateway)
        {
            StartElement(writer, "Routes");
            StartAdded(writer, "Route");
            WriteElement(writer, "Identifier", "1");
            WriteElement(writer, "NextHopAddress", gateway);
            WriteElement(writer, "Prefix", DefaultRoute);
            writer.WriteEndElement();
            writer.WriteEndElement();
        }

        writer.WriteEndElement();
        writer.WriteEndElement();
        writer.WriteEndElement();

        if (network.DnsServers.Count > 0)
        {
            StartComponent(writer, "Microsoft-Windows-DNS-Client", processorArchitecture);
            StartElement(writer, "Interfaces");
            StartAdded(writer, "Interface");
            WriteElement(writer, "Identifier", network.Identifier);
            StartElement(writer, "DNSServerSearchOrder");
            WriteKeyed(writer, "IpAddress", network.DnsServers);
            writer.WriteEndElement();
            writer.WriteEndElement();
            writer.WriteEndElement();
            writer.WriteEndElement();
        }
    }
}
