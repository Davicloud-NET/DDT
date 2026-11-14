// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Xml;
using static DDT.Core.Unattend.UnattendXml;

namespace DDT.Core.Unattend;

internal static class SpecializePass
{
    // A password set through LocalAccounts expires after 42 days on clients. The change Windows then forces would give
    // every PC its own password.
    private const string LiftPasswordAgeCommand = "net accounts /maxpwage:unlimited";

    public static void Write(XmlWriter writer, UnattendSettings settings)
    {
        StartPass(writer, "specialize");

        StartComponent(writer, "Microsoft-Windows-Shell-Setup", settings.ProcessorArchitecture);
        WriteElement(writer, "ComputerName", settings.ComputerName);

        if (settings.ProductKey is { } key)
        {
            WriteElement(writer, "ProductKey", key.Value);
        }

        WriteOptional(writer, "RegisteredOrganization", settings.RegisteredOrganization);
        WriteOptional(writer, "RegisteredOwner", settings.RegisteredOwner);

        // Without TimeZone, Windows picks the zone that matches the locale.
        WriteOptional(writer, "TimeZone", settings.TimeZone);
        writer.WriteEndElement();

        if (settings.LocalAdministrator is not null)
        {
            StartComponent(writer, "Microsoft-Windows-Deployment", settings.ProcessorArchitecture);
            StartElement(writer, "RunSynchronous");
            StartAdded(writer, "RunSynchronousCommand");
            WriteElement(writer, "Order", "1");
            WriteElement(writer, "Path", LiftPasswordAgeCommand);
            writer.WriteEndElement();
            writer.WriteEndElement();
            writer.WriteEndElement();
        }

        if (settings.Network is { } network)
        {
            NetworkComponents.Write(writer, network, settings.ProcessorArchitecture);
        }

        writer.WriteEndElement();
    }
}
