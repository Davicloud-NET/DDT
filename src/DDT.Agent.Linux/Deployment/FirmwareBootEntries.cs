// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Buffers.Binary;
using System.Globalization;
using System.Text;

namespace DDT.Agent.Deployment;

// Describes the firmware's boot entries for the machine log, which then shows the order the firmware has. A
// Boot#### variable is an EFI_LOAD_OPTION (UEFI 2.10, sections 3.1.3 and 10.3). Nothing here throws on what it reads.
public static class FirmwareBootEntries
{
    private const int HeaderLength = 6;
    private const int NodeHeaderLength = 4;
    private const uint LoadOptionActive = 0x00000001;

    private const byte MessagingDevicePath = 0x03;
    private const byte MacAddressSubType = 0x0B;
    private const byte IPv4SubType = 0x0C;
    private const byte IPv6SubType = 0x0D;
    private const byte UriSubType = 0x18;
    private const byte MediaDevicePath = 0x04;
    private const byte HardDriveSubType = 0x01;
    private const byte FilePathSubType = 0x04;
    private const byte EndDevicePath = 0x7F;

    private const int HardDriveNodeLength = 42;
    private const int PartitionNumberOffset = 4;
    private const int SignatureOffset = 24;
    private const int SignatureTypeOffset = 41;
    private const byte GuidSignatureType = 0x02;
    private const int MacLength = 6;

    // One line for the order, then one per entry in it, then where this start came from and the next one goes.
    public static IReadOnlyList<string> Describe(IUefiVariables variables)
    {
        ArgumentNullException.ThrowIfNull(variables);

        List<ushort> order = Numbers(variables.Read("BootOrder"));
        List<string> lines = [order.Count == 0 ? "The firmware has no boot order." : $"The firmware's boot order: {string.Join(", ", order.Select(Name))}."];

        foreach (ushort number in order.Distinct())
        {
            lines.Add(variables.Read(Name(number)) is { } option ? $"  {Name(number)}: {Describe(option)}" : $"  {Name(number)}: no such entry");
        }

        lines.Add(
            $"  This start came from {One(variables.Read("BootCurrent")) ?? "an entry the firmware does not name"}, and the next one " +
            $"comes from {One(variables.Read("BootNext")) ?? "the boot order"}.");

        return lines;
    }

    // The entry's description, whether it is switched off, and what it starts as far as DDT knows the device path.
    public static string Describe(ReadOnlySpan<byte> option)
    {
        if (option.Length < HeaderLength)
        {
            return "not a boot entry";
        }

        bool active = (BinaryPrimitives.ReadUInt32LittleEndian(option) & LoadOptionActive) != 0;
        int listLength = BinaryPrimitives.ReadUInt16LittleEndian(option[4..]);
        ReadOnlySpan<byte> rest = option[HeaderLength..];
        int end = DescriptionEnd(rest);
        string description = Text(end < 0 ? rest : rest[..end]);
        ReadOnlySpan<byte> list = end < 0 ? [] : rest[(end + 2)..];
        string starts = Starts(list[..Math.Min(listLength, list.Length)]);

        return $"{(description.Length == 0 ? "(no description)" : description)}{(starts.Length == 0 ? "" : $", {starts}")}{(active ? "" : ", switched off")}";
    }

    private static string Starts(ReadOnlySpan<byte> list)
    {
        List<string> parts = [];

        while (list.Length >= NodeHeaderLength && list[0] != EndDevicePath)
        {
            int length = BinaryPrimitives.ReadUInt16LittleEndian(list[2..]);

            if (length < NodeHeaderLength || length > list.Length)
            {
                break;
            }

            if (Node(list[..length]) is { } part)
            {
                parts.Add(part);
            }

            list = list[length..];
        }

        return string.Join(' ', parts);
    }

    // Null for the nodes that only say where on the board a device sits.
    private static string? Node(ReadOnlySpan<byte> node) => (node[0], node[1]) switch
    {
        (MediaDevicePath, HardDriveSubType) when node.Length >= HardDriveNodeLength => node[SignatureTypeOffset] == GuidSignatureType
            ? $"partition {BinaryPrimitives.ReadUInt32LittleEndian(node[PartitionNumberOffset..])} ({new Guid(node.Slice(SignatureOffset, 16)):D})"
            : $"partition {BinaryPrimitives.ReadUInt32LittleEndian(node[PartitionNumberOffset..])}",
        (MediaDevicePath, FilePathSubType) => Text(node[NodeHeaderLength..]),
        (MessagingDevicePath, MacAddressSubType) when node.Length >= NodeHeaderLength + MacLength =>
            $"network adapter {Convert.ToHexString(node.Slice(NodeHeaderLength, MacLength))}",
        (MessagingDevicePath, IPv4SubType) => "over IPv4",
        (MessagingDevicePath, IPv6SubType) => "over IPv6",
        (MessagingDevicePath, UriSubType) => "by HTTP",
        _ => null,
    };

    private static string Name(ushort number) => string.Create(CultureInfo.InvariantCulture, $"Boot{number:X4}");

    private static string? One(byte[]? value) => value is { Length: >= 2 } ? Name(BinaryPrimitives.ReadUInt16LittleEndian(value)) : null;

    // An array of UINT16. A stray last byte is ignored.
    private static List<ushort> Numbers(byte[]? value)
    {
        List<ushort> numbers = [];

        for (int offset = 0; value is not null && offset + 1 < value.Length; offset += 2)
        {
            numbers.Add(BinaryPrimitives.ReadUInt16LittleEndian(value.AsSpan(offset)));
        }

        return numbers;
    }

    // Where the description's null character is, or -1 when it has none.
    private static int DescriptionEnd(ReadOnlySpan<byte> text)
    {
        for (int offset = 0; offset + 1 < text.Length; offset += 2)
        {
            if (text[offset] == 0 && text[offset + 1] == 0)
            {
                return offset;
            }
        }

        return -1;
    }

    // UTF-16 up to its null character. What firmware and other systems wrote may hold control characters.
    private static string Text(ReadOnlySpan<byte> utf16)
    {
        string value = Encoding.Unicode.GetString(utf16[..(utf16.Length & ~1)]);
        int end = value.IndexOf('\0', StringComparison.Ordinal);

        return new string([.. (end < 0 ? value : value[..end]).Select(character => char.IsControl(character) ? ' ' : character)]).Trim();
    }
}
