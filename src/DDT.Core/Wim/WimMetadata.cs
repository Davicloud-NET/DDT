// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using DDT.Contracts.Messages;

namespace DDT.Core.Wim;

// Reads the image list of a WIM file without wimlib: the header and the XML resource, which WIM writers store
// uncompressed as UTF-16 LE with a byte order mark.
public static class WimMetadata
{
    private const int HeaderLength = 208;
    private const int HeaderLengthOffset = 8;
    private const int VersionOffset = 12;
    private const int TotalPartsOffset = 42;
    private const int ImageCountOffset = 44;
    private const int XmlResourceOffset = 72;
    private const uint DefaultVersion = 0x10D00;
    private const uint SolidVersion = 0xE00;
    private const byte CompressedResource = 0x04;
    private const byte SolidResource = 0x10;

    // Real image lists are kilobytes; the cap keeps a forged header from allocating gigabytes.
    private const int MaxXmlLength = 16 * 1024 * 1024;


    public static async Task<IReadOnlyList<WimImageInfo>> ReadAsync(Stream wim, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(wim);

        if (!wim.CanRead || !wim.CanSeek)
        {
            throw new ArgumentException("The stream must be readable and seekable.", nameof(wim));
        }

        if (wim.Length < HeaderLength)
        {
            throw new InvalidWimException(ServerMessages.WimNotAWim.With());
        }

        byte[] header = new byte[HeaderLength];
        wim.Position = 0;
        await wim.ReadExactlyAsync(header, cancellationToken).ConfigureAwait(false);

        (uint imageCount, long xmlOffset, int xmlLength) = ReadHeader(header, wim.Length);

        byte[] xml = new byte[xmlLength];
        wim.Position = xmlOffset;
        await wim.ReadExactlyAsync(xml, cancellationToken).ConfigureAwait(false);

        return ReadImages(DecodeXml(xml), imageCount);
    }

    private static (uint ImageCount, long XmlOffset, int XmlLength) ReadHeader(ReadOnlySpan<byte> header, long fileLength)
    {
        ReadOnlySpan<byte> magic = header[..8];

        if (magic.SequenceEqual("WLPWM\0\0\0"u8))
        {
            throw new InvalidWimException(ServerMessages.WimPipable.With());
        }

        if (!magic.SequenceEqual("MSWIM\0\0\0"u8) || BinaryPrimitives.ReadUInt32LittleEndian(header[HeaderLengthOffset..]) != HeaderLength)
        {
            throw new InvalidWimException(ServerMessages.WimNotAWim.With());
        }

        uint version = BinaryPrimitives.ReadUInt32LittleEndian(header[VersionOffset..]);

        if (version is not (DefaultVersion or SolidVersion))
        {
            throw new InvalidWimException(ServerMessages.WimVersion.With("version", version.ToString("X", CultureInfo.InvariantCulture)));
        }

        if (BinaryPrimitives.ReadUInt16LittleEndian(header[TotalPartsOffset..]) != 1)
        {
            throw new InvalidWimException(ServerMessages.WimSplit.With());
        }

        uint imageCount = BinaryPrimitives.ReadUInt32LittleEndian(header[ImageCountOffset..]);

        // A resource header is the size in the file (low 56 bits) with the flags in the top byte, then the
        // offset, then the uncompressed size.
        ReadOnlySpan<byte> xmlResource = header.Slice(XmlResourceOffset, 24);
        byte flags = (byte)(BinaryPrimitives.ReadUInt64LittleEndian(xmlResource) >> 56);
        ulong offset = BinaryPrimitives.ReadUInt64LittleEndian(xmlResource[8..]);
        ulong length = BinaryPrimitives.ReadUInt64LittleEndian(xmlResource[16..]);

        if ((flags & (CompressedResource | SolidResource)) != 0)
        {
            throw new InvalidWimException(ServerMessages.WimCompressedList.With());
        }

        if (length == 0 || offset > (ulong)fileLength || length > (ulong)fileLength - offset)
        {
            throw new InvalidWimException(ServerMessages.WimIncomplete.With());
        }

        if (length > MaxXmlLength)
        {
            throw new InvalidWimException(ServerMessages.WimListTooLarge.With());
        }

        return (imageCount, (long)offset, (int)length);
    }

    private static string DecodeXml(byte[] xml)
    {
        if (xml.Length < 2 || xml.Length % 2 != 0 || xml[0] != 0xFF || xml[1] != 0xFE)
        {
            throw new InvalidWimException(ServerMessages.WimListNotUtf16.With());
        }

        return Encoding.Unicode.GetString(xml, 2, xml.Length - 2);
    }

    private static List<WimImageInfo> ReadImages(string xml, uint imageCount)
    {
        XElement root = Parse(xml);

        if (root.Name.LocalName != "WIM")
        {
            throw new InvalidWimException(ServerMessages.WimListDamaged.With());
        }

        if (root.Elements("ESD").Elements("ENCRYPTED").Any())
        {
            throw new InvalidWimException(ServerMessages.WimEncrypted.With());
        }

        List<WimImageInfo> images = [];

        foreach (XElement image in root.Elements("IMAGE"))
        {
            if (!int.TryParse((string?)image.Attribute("INDEX"), NumberStyles.None, CultureInfo.InvariantCulture, out int index))
            {
                throw new InvalidWimException(ServerMessages.WimListMismatch.With());
            }

            images.Add(ReadImage(image, index));
        }

        if (images.Count != imageCount)
        {
            throw new InvalidWimException(ServerMessages.WimListMismatch.With());
        }

        images.Sort((left, right) => left.Index.CompareTo(right.Index));

        for (int position = 0; position < images.Count; position++)
        {
            if (images[position].Index != position + 1)
            {
                throw new InvalidWimException(ServerMessages.WimListMismatch.With());
            }
        }

        return images;
    }

    private static XElement Parse(string xml)
    {
        XmlReaderSettings settings = new()
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
        };

        try
        {
            using StringReader text = new(xml);
            using XmlReader reader = XmlReader.Create(text, settings);

            return XDocument.Load(reader).Root ?? throw new InvalidWimException(ServerMessages.WimListDamaged.With());
        }
        catch (XmlException exception)
        {
            throw new InvalidWimException(ServerMessages.WimListDamaged.With(), exception);
        }
    }

    private static WimImageInfo ReadImage(XElement image, int index)
    {
        XElement? windows = image.Element("WINDOWS");

        return new WimImageInfo(
            index,
            Text(image.Element("NAME")) ?? "",
            Text(image.Element("DESCRIPTION")),
            Text(windows?.Element("EDITIONID")) ?? Text(image.Element("FLAGS")),
            Architecture(Text(windows?.Element("ARCH"))),
            Version(windows?.Element("VERSION")),
            Text(windows?.Element("LANGUAGES")?.Element("DEFAULT")),
            Bytes(image.Element("TOTALBYTES")),
            Bytes(image.Element("HARDLINKBYTES")));
    }

    private static string? Text(XElement? element)
    {
        string? value = element?.Value.Trim();

        return string.IsNullOrEmpty(value) ? null : value;
    }

    private static string? Architecture(string? value) => value switch
    {
        "0" => "x86",
        "9" => "x64",
        "12" => "arm64",
        _ => null,
    };

    // Major, minor and build are required; the service pack build is appended when present.
    private static string? Version(XElement? version)
    {
        uint? major = Number(version?.Element("MAJOR"));
        uint? minor = Number(version?.Element("MINOR"));
        uint? build = Number(version?.Element("BUILD"));
        uint? servicePackBuild = Number(version?.Element("SPBUILD"));

        if (major is null || minor is null || build is null)
        {
            return null;
        }

        return servicePackBuild is null
            ? string.Create(CultureInfo.InvariantCulture, $"{major}.{minor}.{build}")
            : string.Create(CultureInfo.InvariantCulture, $"{major}.{minor}.{build}.{servicePackBuild}");
    }

    private static uint? Number(XElement? element) =>
        uint.TryParse(Text(element), NumberStyles.None, CultureInfo.InvariantCulture, out uint value) ? value : null;

    private static long Bytes(XElement? element) =>
        long.TryParse(Text(element), NumberStyles.None, CultureInfo.InvariantCulture, out long value) ? value : 0;
}
