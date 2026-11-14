// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Globalization;
using System.Xml;

namespace DDT.Core.Unattend;

// The elements every pass of the answer file is made of.
internal static class UnattendXml
{
    public const string UnattendNamespace = "urn:schemas-microsoft-com:unattend";
    public const string WcmNamespace = "http://schemas.microsoft.com/WMIConfig/2002/State";
    private const string XsiNamespace = "http://www.w3.org/2001/XMLSchema-instance";
    private const string PublicKeyToken = "31bf3856ad364e35";

    public static void StartPass(XmlWriter writer, string pass)
    {
        StartElement(writer, "settings");
        writer.WriteAttributeString("pass", pass);
    }

    public static void StartComponent(XmlWriter writer, string name, string processorArchitecture)
    {
        StartElement(writer, "component");
        writer.WriteAttributeString("name", name);
        writer.WriteAttributeString("processorArchitecture", processorArchitecture);
        writer.WriteAttributeString("publicKeyToken", PublicKeyToken);
        writer.WriteAttributeString("language", "neutral");
        writer.WriteAttributeString("versionScope", "nonSxS");
        writer.WriteAttributeString("xmlns", "wcm", null, WcmNamespace);
        writer.WriteAttributeString("xmlns", "xsi", null, XsiNamespace);
    }

    public static void StartElement(XmlWriter writer, string name) => writer.WriteStartElement(name, UnattendNamespace);

    // A list entry Setup adds to what the image has.
    public static void StartAdded(XmlWriter writer, string name)
    {
        StartElement(writer, name);
        writer.WriteAttributeString("action", WcmNamespace, "add");
    }

    public static void WriteElement(XmlWriter writer, string name, string value) =>
        writer.WriteElementString(name, UnattendNamespace, value);

    public static void WriteOptional(XmlWriter writer, string name, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            WriteElement(writer, name, value);
        }
    }

    // Numbered entries, which Setup keeps in that order.
    public static void WriteKeyed(XmlWriter writer, string name, IReadOnlyList<string> values)
    {
        for (int index = 0; index < values.Count; index++)
        {
            StartAdded(writer, name);
            writer.WriteAttributeString("keyValue", WcmNamespace, (index + 1).ToString(CultureInfo.InvariantCulture));
            writer.WriteString(values[index]);
            writer.WriteEndElement();
        }
    }

    public static string Flag(bool value) => value ? "true" : "false";
}
