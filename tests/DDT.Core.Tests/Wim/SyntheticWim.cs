using System.Buffers.Binary;
using System.Text;

namespace DDT.Core.Tests.Wim;

// The parts of a WIM file WimMetadata reads: a 208-byte header and an uncompressed XML resource, placed after a
// few bytes that stand in for the image data.
public sealed record SyntheticWim
{
    private const int HeaderLength = 208;
    private const int Filler = 64;

    public string Magic { get; init; } = "MSWIM\0\0\0";

    public uint HeaderLengthField { get; init; } = HeaderLength;

    public uint Version { get; init; } = 0x10D00;

    public ushort TotalParts { get; init; } = 1;

    public uint ImageCount { get; init; } = 1;

    public byte XmlFlags { get; init; }

    public byte[] Xml { get; init; } = Utf16("<WIM><IMAGE INDEX=\"1\"><NAME>Image</NAME></IMAGE></WIM>");

    // Declares more XML than the file holds, as in a truncated upload.
    public long MissingXmlBytes { get; init; }

    public static byte[] Utf16(string xml) => [0xFF, 0xFE, .. Encoding.Unicode.GetBytes(xml)];

    public static SyntheticWim WithImages(params string[] images) => new()
    {
        ImageCount = (uint)images.Length,
        Xml = Utf16($"<WIM><TOTALBYTES>1000</TOTALBYTES>{string.Concat(images)}</WIM>"),
    };

    public MemoryStream ToStream()
    {
        byte[] file = new byte[HeaderLength + Filler + Xml.Length];
        Span<byte> header = file.AsSpan(0, HeaderLength);
        ulong xmlLength = (ulong)(Xml.Length + MissingXmlBytes);

        Encoding.ASCII.GetBytes(Magic).CopyTo(header);
        BinaryPrimitives.WriteUInt32LittleEndian(header[8..], HeaderLengthField);
        BinaryPrimitives.WriteUInt32LittleEndian(header[12..], Version);
        BinaryPrimitives.WriteUInt32LittleEndian(header[16..], 0x2 | 0x40000);
        BinaryPrimitives.WriteUInt32LittleEndian(header[20..], 32768);
        BinaryPrimitives.WriteUInt16LittleEndian(header[40..], 1);
        BinaryPrimitives.WriteUInt16LittleEndian(header[42..], TotalParts);
        BinaryPrimitives.WriteUInt32LittleEndian(header[44..], ImageCount);
        BinaryPrimitives.WriteUInt64LittleEndian(header[72..], xmlLength | ((ulong)XmlFlags << 56));
        BinaryPrimitives.WriteUInt64LittleEndian(header[80..], HeaderLength + Filler);
        BinaryPrimitives.WriteUInt64LittleEndian(header[88..], xmlLength);
        Xml.CopyTo(file, HeaderLength + Filler);

        return new MemoryStream(file, writable: false);
    }
}
