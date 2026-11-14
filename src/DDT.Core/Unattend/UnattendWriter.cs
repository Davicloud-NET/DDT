// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Text;
using System.Xml;

namespace DDT.Core.Unattend;

public static class UnattendWriter
{
    public static string Write(UnattendSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentException.ThrowIfNullOrWhiteSpace(settings.ProcessorArchitecture);
        ArgumentException.ThrowIfNullOrWhiteSpace(settings.ComputerName);
        ArgumentException.ThrowIfNullOrWhiteSpace(settings.UiLanguage);
        ArgumentException.ThrowIfNullOrWhiteSpace(settings.Locale);
        ArgumentException.ThrowIfNullOrWhiteSpace(settings.Keyboard);
        ArgumentNullException.ThrowIfNull(settings.Oobe);

        if (settings.LocalAdministrator is { } administrator)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(administrator.Name);
            ArgumentException.ThrowIfNullOrEmpty(administrator.Password);
        }

        if (settings.ProductKey is { } key && !ProductKey.IsValid(key.Value))
        {
            // Setup fails on a key it can't read, so a wrong one never gets that far. The message leaves the key out.
            throw new ArgumentException("The product key is not five groups of five letters or digits.", nameof(settings));
        }

        if (settings.Network is { } network)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(network.Identifier);

            if (network.Addresses.Count == 0)
            {
                throw new ArgumentException("A static network needs an address.", nameof(settings));
            }
        }

        XmlWriterSettings xmlSettings = new()
        {
            Encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            Indent = true,
            IndentChars = "  ",
            NewLineChars = "\r\n",
            // The default would rewrite a line break inside a password and change the value Setup reads.
            NewLineHandling = NewLineHandling.Entitize,
        };

        using MemoryStream stream = new();
        using XmlWriter writer = XmlWriter.Create(stream, xmlSettings);

        writer.WriteStartDocument();
        writer.WriteStartElement("unattend", UnattendXml.UnattendNamespace);
        SpecializePass.Write(writer, settings);
        OobeSystemPass.Write(writer, settings);
        writer.WriteEndElement();
        writer.WriteEndDocument();
        writer.Flush();

        return Encoding.UTF8.GetString(stream.ToArray());
    }
}
