using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using System.Xml;
using System.Xml.Linq;

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

    private const string NotAWim = "This file is not a WIM image.";
    private const string Incomplete = "This WIM file is incomplete or damaged.";
    private const string DamagedXml = "The image list in this WIM file is damaged.";
    private const string XmlDoesNotMatchHeader = "The image list in this WIM file does not match its header.";

    public static async Task<IReadOnlyList<WimImageInfo>> ReadAsync(Stream wim, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(wim);

        if (!wim.CanRead || !wim.CanSeek)
        {
            throw new ArgumentException("The stream must be readable and seekable.", nameof(wim));
        }

        if (wim.Length < HeaderLength)
        {
            throw new InvalidWimException(NotAWim);
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
            throw new InvalidWimException("Pipable WIM files are not supported. Export the image into a regular WIM first.");
        }

        if (!magic.SequenceEqual("MSWIM\0\0\0"u8) || BinaryPrimitives.ReadUInt32LittleEndian(header[HeaderLengthOffset..]) != HeaderLength)
        {
            throw new InvalidWimException(NotAWim);
        }

        uint version = BinaryPrimitives.ReadUInt32LittleEndian(header[VersionOffset..]);

        if (version is not (DefaultVersion or SolidVersion))
        {
            throw new InvalidWimException($"This WIM file uses format version 0x{version:X}, which DDT does not support.");
        }

        if (BinaryPrimitives.ReadUInt16LittleEndian(header[TotalPartsOffset..]) != 1)
        {
            throw new InvalidWimException("Split WIM files (.swm) are not supported. Export the image into a single WIM first.");
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
            throw new InvalidWimException("This WIM file stores its image list compressed, which DDT cannot read.");
        }

        if (length == 0 || offset > (ulong)fileLength || length > (ulong)fileLength - offset)
        {
            throw new InvalidWimException(Incomplete);
        }

        if (length > MaxXmlLength)
        {
            throw new InvalidWimException("The image list in this WIM file is too large.");
        }

        return (imageCount, (long)offset, (int)length);
    }

    private static string DecodeXml(byte[] xml)
    {
        if (xml.Length < 2 || xml.Length % 2 != 0 || xml[0] != 0xFF || xml[1] != 0xFE)
        {
            throw new InvalidWimException("The image list in this WIM file is not UTF-16 text, which DDT cannot read.");
        }

        return Encoding.Unicode.GetString(xml, 2, xml.Length - 2);
    }

    private static List<WimImageInfo> ReadImages(string xml, uint imageCount)
    {
        XElement root = Parse(xml);

        if (root.Name.LocalName != "WIM")
        {
            throw new InvalidWimException(DamagedXml);
        }

        if (root.Elements("ESD").Elements("ENCRYPTED").Any())
        {
            throw new InvalidWimException("This image is encrypted (an ESD from Windows Update) and cannot be applied.");
        }

        List<WimImageInfo> images = [];

        foreach (XElement image in root.Elements("IMAGE"))
        {
            if (!int.TryParse((string?)image.Attribute("INDEX"), NumberStyles.None, CultureInfo.InvariantCulture, out int index))
            {
                throw new InvalidWimException(XmlDoesNotMatchHeader);
            }

            images.Add(ReadImage(image, index));
        }

        if (images.Count != imageCount)
        {
            throw new InvalidWimException(XmlDoesNotMatchHeader);
        }

        images.Sort((left, right) => left.Index.CompareTo(right.Index));

        for (int position = 0; position < images.Count; position++)
        {
            if (images[position].Index != position + 1)
            {
                throw new InvalidWimException(XmlDoesNotMatchHeader);
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

            return XDocument.Load(reader).Root ?? throw new InvalidWimException(DamagedXml);
        }
        catch (XmlException exception)
        {
            throw new InvalidWimException(DamagedXml, exception);
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
