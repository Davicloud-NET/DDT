// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Buffers.Binary;
using System.Text;

namespace DDT.Agent.Deployment;

// EFI_LOAD_OPTION, the value of a Boot#### variable (UEFI 2.10, sections 3.1.3 and 10.3): UINT32 attributes, UINT16
// length of the device path list, the description as null-terminated UTF-16, the device paths, then optional data.
public static class EfiLoadOption
{
    private const uint LoadOptionActive = 0x00000001;

    private const byte MediaDevicePath = 0x04;
    private const byte HardDriveSubType = 0x01;
    private const byte FilePathSubType = 0x04;
    private const byte EndDevicePath = 0x7F;
    private const byte EndEntireDevicePathSubType = 0xFF;

    private const byte GptPartitionFormat = 0x02;
    private const byte GuidSignatureType = 0x02;

    private const int HeaderLength = 6;
    private const int NodeHeaderLength = 4;
    private const int HardDriveNodeLength = 42;
    private const int EndNodeLength = 4;

    private const int PartitionNumberOffset = 4;
    private const int PartitionStartOffset = 8;
    private const int PartitionSizeOffset = 16;
    private const int SignatureOffset = 24;
    private const int SignatureLength = 16;
    private const int PartitionFormatOffset = 40;
    private const int SignatureTypeOffset = 41;

    // An active option that starts path on the partition esp describes, with no optional data.
    public static byte[] Build(string description, EspPartition esp, string path)
    {
        ArgumentNullException.ThrowIfNull(description);
        ArgumentNullException.ThrowIfNull(esp);
        ArgumentException.ThrowIfNullOrEmpty(path);

        int descriptionLength = (description.Length + 1) * 2;
        int fileNodeLength = NodeHeaderLength + ((path.Length + 1) * 2);
        int listLength = HardDriveNodeLength + fileNodeLength + EndNodeLength;

        if (listLength > ushort.MaxValue)
        {
            throw new ArgumentException("The path is too long for a load option.", nameof(path));
        }

        byte[] option = new byte[HeaderLength + descriptionLength + listLength];
        Span<byte> span = option;

        BinaryPrimitives.WriteUInt32LittleEndian(span, LoadOptionActive);
        BinaryPrimitives.WriteUInt16LittleEndian(span[4..], (ushort)listLength);
        Encoding.Unicode.GetBytes(description, span[HeaderLength..]);

        Span<byte> node = span[(HeaderLength + descriptionLength)..];
        WriteNodeHeader(node, MediaDevicePath, HardDriveSubType, HardDriveNodeLength);
        BinaryPrimitives.WriteUInt32LittleEndian(node[PartitionNumberOffset..], esp.PartitionNumber);
        BinaryPrimitives.WriteUInt64LittleEndian(node[PartitionStartOffset..], esp.StartLba);
        BinaryPrimitives.WriteUInt64LittleEndian(node[PartitionSizeOffset..], esp.SizeLba);

        // Guid.ToByteArray gives EFI_GUID's byte order: the first three fields little-endian.
        esp.PartitionId.ToByteArray().CopyTo(node[SignatureOffset..]);
        node[PartitionFormatOffset] = GptPartitionFormat;
        node[SignatureTypeOffset] = GuidSignatureType;

        node = node[HardDriveNodeLength..];
        WriteNodeHeader(node, MediaDevicePath, FilePathSubType, fileNodeLength);
        Encoding.Unicode.GetBytes(path, node[NodeHeaderLength..]);

        node = node[fileNodeLength..];
        WriteNodeHeader(node, EndDevicePath, EndEntireDevicePathSubType, EndNodeLength);

        return option;
    }

    // Whether the option starts path on the GPT partition partitionId. The path is compared ignoring case, and a null
    // path matches any file. Never throws, because the option comes from the firmware and may hold anything.
    public static bool PointsAt(ReadOnlySpan<byte> option, Guid partitionId, string? path)
    {
        if (option.Length < HeaderLength)
        {
            return false;
        }

        int listLength = BinaryPrimitives.ReadUInt16LittleEndian(option[4..]);
        int listStart = DescriptionEnd(option);

        if (listStart < 0 || listLength > option.Length - listStart)
        {
            return false;
        }

        ReadOnlySpan<byte> list = option.Slice(listStart, listLength);
        bool onPartition = false;
        bool toPath = false;

        while (list.Length >= NodeHeaderLength)
        {
            int length = BinaryPrimitives.ReadUInt16LittleEndian(list[2..]);

            if (length < NodeHeaderLength || length > list.Length)
            {
                break;
            }

            ReadOnlySpan<byte> node = list[..length];

            if (node[0] == MediaDevicePath && node[1] == HardDriveSubType && length >= HardDriveNodeLength
                && node[SignatureTypeOffset] == GuidSignatureType)
            {
                onPartition |= new Guid(node.Slice(SignatureOffset, SignatureLength)) == partitionId;
            }
            else if (node[0] == MediaDevicePath && node[1] == FilePathSubType)
            {
                toPath |= string.Equals(PathOf(node[NodeHeaderLength..]), path, StringComparison.OrdinalIgnoreCase);
            }

            list = list[length..];
        }

        return onPartition && (toPath || path is null);
    }

    private static void WriteNodeHeader(Span<byte> node, byte type, byte subType, int length)
    {
        node[0] = type;
        node[1] = subType;
        BinaryPrimitives.WriteUInt16LittleEndian(node[2..], (ushort)length);
    }

    // Where the device paths start: right after the description's null character, or -1 when it has none.
    private static int DescriptionEnd(ReadOnlySpan<byte> option)
    {
        for (int offset = HeaderLength; offset + 1 < option.Length; offset += 2)
        {
            if (option[offset] == 0 && option[offset + 1] == 0)
            {
                return offset + 2;
            }
        }

        return -1;
    }

    private static string PathOf(ReadOnlySpan<byte> text)
    {
        string value = Encoding.Unicode.GetString(text[..(text.Length & ~1)]);
        int end = value.IndexOf('\0', StringComparison.Ordinal);

        return end < 0 ? value : value[..end];
    }
}
