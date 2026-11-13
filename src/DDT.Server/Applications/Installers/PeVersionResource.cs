// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Buffers.Binary;
using System.Globalization;
using System.Text;

namespace DDT.Server.Applications.Installers;

// Reads the VS_VERSIONINFO resource from the bytes of the section that holds the resource directory. FileVersionInfo
// can't, since it reads only managed assemblies on Linux.
internal static class PeVersionResource
{
    private const uint VersionResourceType = 16;
    private const uint FixedInfoSignature = 0xFEEF04BD;
    private const int FixedInfoLength = 52;
    private const int NodeHeaderLength = 6;

    public static PeVersionInfo Read(ReadOnlySpan<byte> section, PeSection layout, uint resourceRva) =>
        PeResourceDirectory.Find(section, layout, resourceRva, VersionResourceType, null) is (int start, int length)
            ? Parse(section.Slice(start, length))
            : PeVersionInfo.None;

    private static PeVersionInfo Parse(ReadOnlySpan<byte> info)
    {
        if (Node(info, 0, info.Length) is not { } root)
        {
            return PeVersionInfo.None;
        }

        Dictionary<string, string> strings = new(StringComparer.OrdinalIgnoreCase);

        foreach (VersionNode child in Children(info, root))
        {
            if (child.Key == "StringFileInfo")
            {
                foreach (VersionNode table in Children(info, child))
                {
                    foreach (VersionNode text in Children(info, table))
                    {
                        strings.TryAdd(text.Key, Text(info, text));
                    }
                }
            }
        }

        return new PeVersionInfo(
            NonEmpty(strings.GetValueOrDefault("ProductName")) ?? NonEmpty(strings.GetValueOrDefault("FileDescription")),
            NonEmpty(strings.GetValueOrDefault("CompanyName")),
            NonEmpty(strings.GetValueOrDefault("ProductVersion")) ?? FixedProductVersion(info, root));
    }

    private static IEnumerable<VersionNode> Children(ReadOnlySpan<byte> info, VersionNode parent)
    {
        List<VersionNode> children = [];

        for (int offset = parent.ChildrenStart; Node(info, offset, parent.End) is { } child; offset = Align(child.End))
        {
            children.Add(child);
        }

        return children;
    }

    // A node is its length, its value's length, its type, its key in UTF-16, then its value and its children, each
    // aligned to 4 bytes.
    private static VersionNode? Node(ReadOnlySpan<byte> info, int offset, int limit)
    {
        if (offset + NodeHeaderLength > limit)
        {
            return null;
        }

        int length = BinaryPrimitives.ReadUInt16LittleEndian(info[offset..]);
        int valueLength = BinaryPrimitives.ReadUInt16LittleEndian(info[(offset + 2)..]);
        bool isText = BinaryPrimitives.ReadUInt16LittleEndian(info[(offset + 4)..]) == 1;
        int end = offset + length;

        if (length < NodeHeaderLength || end > limit)
        {
            return null;
        }

        int keyStart = offset + NodeHeaderLength;
        int keyEnd = keyStart;

        while (keyEnd + 2 <= end && BinaryPrimitives.ReadUInt16LittleEndian(info[keyEnd..]) != 0)
        {
            keyEnd += 2;
        }

        int valueStart = Math.Min(Align(keyEnd + 2), end);
        int valueBytes = Math.Min(isText ? valueLength * 2 : valueLength, end - valueStart);
        string key = Encoding.Unicode.GetString(info[keyStart..keyEnd]);

        return new VersionNode(key, valueStart, valueBytes, Math.Min(Align(valueStart + valueBytes), end), end);
    }

    private static string Text(ReadOnlySpan<byte> info, VersionNode node) =>
        Encoding.Unicode.GetString(info.Slice(node.ValueStart, node.ValueLength & ~1)).TrimEnd('\0').Trim();

    private static string? FixedProductVersion(ReadOnlySpan<byte> info, VersionNode root)
    {
        if (root.ValueLength < FixedInfoLength || BinaryPrimitives.ReadUInt32LittleEndian(info[root.ValueStart..]) != FixedInfoSignature)
        {
            return null;
        }

        uint high = BinaryPrimitives.ReadUInt32LittleEndian(info[(root.ValueStart + 16)..]);
        uint low = BinaryPrimitives.ReadUInt32LittleEndian(info[(root.ValueStart + 20)..]);

        return string.Create(CultureInfo.InvariantCulture, $"{high >> 16}.{high & 0xFFFF}.{low >> 16}.{low & 0xFFFF}");
    }

    private static string? NonEmpty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;

    private static int Align(int offset) => (offset + 3) & ~3;

    private sealed record VersionNode(string Key, int ValueStart, int ValueLength, int ChildrenStart, int End);
}
