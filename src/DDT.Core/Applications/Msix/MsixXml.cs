// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Xml;
using System.Xml.Linq;

namespace DDT.Core.Applications.Msix;

// Reads a manifest or a licence: at most MaxBytes, and no DTD, so an entity cannot grow it.
internal static class MsixXml
{
    // Real manifests have a few hundred kilobytes at most.
    public const int MaxBytes = 4 * 1024 * 1024;

    // Null when the data is longer than MaxBytes or no XML.
    public static XElement? Root(Stream data)
    {
        if (ReadAtMost(data, MaxBytes) is not { } bytes)
        {
            return null;
        }

        XmlReaderSettings settings = new()
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            MaxCharactersInDocument = MaxBytes,
        };

        try
        {
            using MemoryStream text = new(bytes, writable: false);
            using XmlReader reader = XmlReader.Create(text, settings);

            return XDocument.Load(reader).Root;
        }
        catch (XmlException)
        {
            return null;
        }
    }

    // A Microsoft Store licence, which DISM takes with /LicensePath.
    public static bool IsLicense(Stream data) => Root(data)?.Name.LocalName == "License";

    public static XElement? Child(XElement? parent, string localName) =>
        parent?.Elements().FirstOrDefault(element => element.Name.LocalName == localName);

    private static byte[]? ReadAtMost(Stream data, int max)
    {
        using MemoryStream copy = new();
        byte[] buffer = new byte[81920];
        int count;

        while ((count = data.Read(buffer, 0, buffer.Length)) > 0)
        {
            if (copy.Length + count > max)
            {
                return null;
            }

            copy.Write(buffer, 0, count);
        }

        return copy.ToArray();
    }
}
