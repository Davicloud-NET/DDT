// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Buffers.Binary;
using System.Text;

namespace DDT.Server.Tests.Applications;

// Minimal PE32+ installers: headers, a .text section, a .rsrc section with a version resource, an optional .wixburn
// section, then the overlay.
internal static class TestExe
{
    private const int PeOffset = 0x40;
    private const int OptionalHeader = PeOffset + 24;
    private const int HeadersLength = 0x400;
    private const uint ResourceRva = 0x2000;

    public static byte[] Create(TestExeOptions options)
    {
        byte[] text = new byte[0x200];
        byte[] resources = Align(Resources(options), 0x200);
        List<(string Name, uint Rva, byte[] Data)> sections = [(".text", 0x1000, text), (".rsrc", ResourceRva, resources)];

        if (options.BurnSection is { } burn)
        {
            sections.Add((".wixburn", ResourceRva + (uint)Align(resources.Length, 0x1000), Align(burn, 0x200)));
        }

        byte[] headers = Headers(sections, resources.Length);

        return [.. headers, .. sections.SelectMany(section => section.Data), .. options.Overlay];
    }

    // WiX's BURN_SECTION_HEADER: magic, version 2, the bundle's GUID, then sizes DDT doesn't read.
    public static byte[] BurnHeader(Guid bundle)
    {
        byte[] header = new byte[64];
        BinaryPrimitives.WriteUInt32LittleEndian(header, 0x00F14300);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(4), 2);
        bundle.TryWriteBytes(header.AsSpan(8));

        return header;
    }

    private static byte[] Headers(List<(string Name, uint Rva, byte[] Data)> sections, int resourceLength)
    {
        byte[] file = new byte[HeadersLength];
        Span<byte> span = file;
        span[0] = (byte)'M';
        span[1] = (byte)'Z';
        BinaryPrimitives.WriteInt32LittleEndian(span[0x3C..], PeOffset);
        BinaryPrimitives.WriteUInt32LittleEndian(span[PeOffset..], 0x00004550);
        BinaryPrimitives.WriteUInt16LittleEndian(span[(PeOffset + 4)..], 0x8664);
        BinaryPrimitives.WriteUInt16LittleEndian(span[(PeOffset + 6)..], (ushort)sections.Count);
        BinaryPrimitives.WriteUInt16LittleEndian(span[(PeOffset + 20)..], 240);
        BinaryPrimitives.WriteUInt16LittleEndian(span[OptionalHeader..], 0x20B);
        BinaryPrimitives.WriteInt32LittleEndian(span[(OptionalHeader + 60)..], HeadersLength);
        BinaryPrimitives.WriteUInt32LittleEndian(span[(OptionalHeader + 108)..], 16);
        BinaryPrimitives.WriteUInt32LittleEndian(span[(OptionalHeader + 112 + 16)..], ResourceRva);
        BinaryPrimitives.WriteInt32LittleEndian(span[(OptionalHeader + 112 + 20)..], resourceLength);

        int raw = HeadersLength;

        for (int index = 0; index < sections.Count; index++)
        {
            (string name, uint rva, byte[] data) = sections[index];
            Span<byte> header = span[(OptionalHeader + 240 + (index * 40))..];
            Encoding.ASCII.GetBytes(name).CopyTo(header);
            BinaryPrimitives.WriteInt32LittleEndian(header[8..], data.Length);
            BinaryPrimitives.WriteUInt32LittleEndian(header[12..], rva);
            BinaryPrimitives.WriteInt32LittleEndian(header[16..], data.Length);
            BinaryPrimitives.WriteInt32LittleEndian(header[20..], raw);
            raw += data.Length;
        }

        return file;
    }

    // The resource directory: per resource a type, a name and a language 1033 level, then the data entries and data.
    private static byte[] Resources(TestExeOptions options)
    {
        List<(uint Type, uint Name, byte[] Data)> resources = [];

        if (options.InnoOffsetTable is { } table)
        {
            resources.Add((10, 11111, table));
        }

        if (options.VersionStrings.Count > 0 || options.FixedVersion is not null)
        {
            resources.Add((16, 1, VersionInfo(options)));
        }

        if (resources.Count == 0)
        {
            return [.. new byte[16], .. options.MoreResources];
        }

        int levels = 16 + (resources.Count * 8);
        byte[] directory = new byte[levels + (resources.Count * 64)];
        List<byte> data = [];
        BinaryPrimitives.WriteUInt16LittleEndian(directory.AsSpan(14), (ushort)resources.Count);

        for (int index = 0; index < resources.Count; index++)
        {
            (uint type, uint name, byte[] bytes) = resources[index];
            int names = levels + (index * 64);
            Entry(directory, 16 + (index * 8), type, 0x80000000 | (uint)names);
            Directory(directory, names, name, 0x80000000 | (uint)(names + 24));
            Directory(directory, names + 24, 1033, (uint)(names + 48));
            BinaryPrimitives.WriteUInt32LittleEndian(directory.AsSpan(names + 48), ResourceRva + (uint)(directory.Length + data.Count));
            BinaryPrimitives.WriteInt32LittleEndian(directory.AsSpan(names + 52), bytes.Length);
            data.AddRange(Align(bytes, 4));
        }

        return [.. directory, .. data, .. options.MoreResources];
    }

    private static void Directory(byte[] resources, int offset, uint id, uint target)
    {
        BinaryPrimitives.WriteUInt16LittleEndian(resources.AsSpan(offset + 14), 1);
        Entry(resources, offset + 16, id, target);
    }

    private static void Entry(byte[] resources, int offset, uint id, uint target)
    {
        BinaryPrimitives.WriteUInt32LittleEndian(resources.AsSpan(offset), id);
        BinaryPrimitives.WriteUInt32LittleEndian(resources.AsSpan(offset + 4), target);
    }

    private static byte[] VersionInfo(TestExeOptions options)
    {
        byte[] fixedInfo = [];

        if (options.FixedVersion is { } version)
        {
            fixedInfo = new byte[52];
            BinaryPrimitives.WriteUInt32LittleEndian(fixedInfo, 0xFEEF04BD);
            BinaryPrimitives.WriteUInt32LittleEndian(fixedInfo.AsSpan(4), 0x00010000);
            BinaryPrimitives.WriteUInt32LittleEndian(fixedInfo.AsSpan(16), (uint)((version.Major << 16) | version.Minor));
            BinaryPrimitives.WriteUInt32LittleEndian(fixedInfo.AsSpan(20), (uint)((version.Build << 16) | version.Revision));
        }

        byte[][] strings = [.. options.VersionStrings.Select(pair => Node(pair.Key, Encoding.Unicode.GetBytes(pair.Value + "\0"), pair.Value.Length + 1, true))];
        byte[] table = Node("040904B0", [], 0, true, strings);

        return Node("VS_VERSION_INFO", fixedInfo, fixedInfo.Length, false, Node("StringFileInfo", [], 0, true, table));
    }

    private static byte[] Node(string key, byte[] value, int valueLength, bool isText, params byte[][] children)
    {
        List<byte> node = [0, 0, (byte)valueLength, (byte)(valueLength >> 8), (byte)(isText ? 1 : 0), 0];
        node.AddRange(Encoding.Unicode.GetBytes(key + "\0"));
        node.AddRange(new byte[Padding(node.Count)]);
        node.AddRange(value);

        foreach (byte[] child in children)
        {
            node.AddRange(new byte[Padding(node.Count)]);
            node.AddRange(child);
        }

        byte[] bytes = [.. node];
        BinaryPrimitives.WriteUInt16LittleEndian(bytes, (ushort)bytes.Length);

        return bytes;
    }

    private static int Padding(int length) => (4 - (length % 4)) % 4;

    private static byte[] Align(byte[] data, int alignment) => [.. data, .. new byte[(alignment - (data.Length % alignment)) % alignment]];

    private static int Align(int length, int alignment) => length + ((alignment - (length % alignment)) % alignment);
}
