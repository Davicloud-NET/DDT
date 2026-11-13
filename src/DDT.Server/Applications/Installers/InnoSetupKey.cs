// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Buffers.Binary;
using System.Text;
using DDT.Core;

namespace DDT.Server.Applications.Installers;

// The uninstall key an Inno Setup installer registers, <AppId>_is1, from the strings at the start of its setup header.
// Every doubt gives null: an unknown version, a CRC that does not match, an encrypted header, an AppId with a constant,
// a setup that may not write the key.
internal static class InnoSetupKey
{
    private const int MaxStringBytes = 64 * 1024;
    private const long MaxHeaderBytes = 16 * 1024 * 1024;
    private const int EncryptionHeaderLength = 49;
    private const byte FullEncryption = 2;
    private const int AppIdIndex = 2;

    // Setup shortens a longer AppId with a CRC (GetUninstallRegKeyBaseName); that is not worth guessing.
    private const int MaxBaseNameLength = 57;

    public static string? Read(Stream file, ReadOnlySpan<byte> resources, PeSection section, uint resourceRva)
    {
        ArgumentNullException.ThrowIfNull(file);

        return PeResourceDirectory.Find(resources, section, resourceRva, InnoLoaderTable.ResourceType, InnoLoaderTable.ResourceName) is (int start, int length)
            && InnoLoaderTable.Offset0(resources.Slice(start, length)) is { } offset0
            ? FromSetupData(file, offset0)
            : null;
    }

    // offset is where setup-0.bin starts in the file.
    public static string? FromSetupData(Stream file, long offset)
    {
        ArgumentNullException.ThrowIfNull(file);

        if (InnoSetupVersions.Of(file.ReadAtMost(offset, InnoSetupVersions.IdLength)) is not { } layout)
        {
            return null;
        }

        long at = offset + InnoSetupVersions.IdLength;

        if (layout.EncryptionHeader)
        {
            byte[] encryption = new byte[4 + EncryptionHeaderLength];
            file.ReadExactlyAt(at, encryption);

            if (Crc32.Append(0, encryption.AsSpan(4)) != BinaryPrimitives.ReadUInt32LittleEndian(encryption) || encryption[4] == FullEncryption)
            {
                return null;
            }

            at += encryption.Length;
        }

        return BlockAt(file, at, layout.LongBlockSize) is (long data, long storedSize, bool compressed)
            ? FromHeader(file, data, storedSize, compressed, layout)
            : null;
    }

    // AppId as Setup expands it: {{ is a brace, any other brace starts a constant only Setup can expand.
    public static string? KeyOf(string appId)
    {
        ArgumentNullException.ThrowIfNull(appId);

        StringBuilder expanded = new();

        for (int index = 0; index < appId.Length; index++)
        {
            char character = appId[index];

            if (character == '{')
            {
                if (index + 1 >= appId.Length || appId[index + 1] != '{')
                {
                    return null;
                }

                index++;
            }
            else if (char.IsControl(character) || character == '\\')
            {
                return null;
            }

            expanded.Append(character);
        }

        return expanded.Length is > 0 and <= MaxBaseNameLength && expanded.ToString().Trim().Length == expanded.Length ? $"{expanded}_is1" : null;
    }

    // The CRC, the stored size and the compressed flag of the block header (TCompressedBlockHeader).
    private static (long Data, long StoredSize, bool Compressed)? BlockAt(Stream file, long at, bool longSize)
    {
        int sizeLength = longSize ? 8 : 4;
        byte[] header = new byte[4 + sizeLength + 1];
        file.ReadExactlyAt(at, header);
        ReadOnlySpan<byte> fields = header.AsSpan(4);
        long storedSize = longSize ? BinaryPrimitives.ReadInt64LittleEndian(fields) : BinaryPrimitives.ReadUInt32LittleEndian(fields);
        long data = at + header.Length;

        return Crc32.Append(0, fields) == BinaryPrimitives.ReadUInt32LittleEndian(header) && storedSize > 0 && storedSize <= file.Length - data
            && fields[sizeLength] <= 1
            ? (data, storedSize, fields[sizeLength] == 1)
            : null;
    }

    private static string? FromHeader(Stream file, long data, long storedSize, bool compressed, InnoSetupLayout layout)
    {
        using InnoBlockStream chunks = new(file, data, storedSize);
        using Stream? header = compressed ? InstallerLzma.Open(chunks, MaxHeaderBytes) : chunks;

        if (header is null)
        {
            return null;
        }

        string? appId = null;
        string? createKey = null;

        for (int index = 0; index <= layout.CreateKeyIndex + 1; index++)
        {
            string? text = ReadString(header, layout.Unicode);

            if (index == AppIdIndex)
            {
                appId = text;
            }
            else if (index == layout.CreateKeyIndex)
            {
                createKey = text;
            }
            else if (index == layout.CreateKeyIndex + 1 && !(IsYes(createKey) && IsYes(text)))
            {
                return null;
            }
        }

        return appId is null ? null : KeyOf(appId);
    }

    // A length in bytes, then UTF-16 or ANSI. ANSI with anything past ASCII reads as null: its code page is unknown.
    private static string? ReadString(Stream header, bool unicode)
    {
        byte[] lengthBytes = new byte[4];
        header.ReadExactly(lengthBytes);
        uint length = BinaryPrimitives.ReadUInt32LittleEndian(lengthBytes);

        if (length > MaxStringBytes || (unicode && length % 2 != 0))
        {
            throw new InvalidDataException("An Inno Setup header string is too long.");
        }

        byte[] bytes = new byte[length];
        header.ReadExactly(bytes);

        return unicode ? Encoding.Unicode.GetString(bytes) : Array.TrueForAll(bytes, value => value < 0x80) ? Encoding.ASCII.GetString(bytes) : null;
    }

    // Setup evaluates the directive: yes, true and 1 are true, anything else may be false or a Check function.
    private static bool IsYes(string? value) =>
        value is not null && (value.Equals("yes", StringComparison.OrdinalIgnoreCase) || value.Equals("true", StringComparison.OrdinalIgnoreCase) || value == "1");
}
