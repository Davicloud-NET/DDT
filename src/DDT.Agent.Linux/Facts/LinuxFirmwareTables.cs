// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Buffers.Binary;
using System.Text;

namespace DDT.Agent.Facts;

// The firmware's tables as Linux shows them under /sys/firmware, in the layout Windows gives them.
public sealed class LinuxFirmwareTables(string root) : IFirmwareTables
{
    private const int RawHeaderLength = 8;
    private const int SignatureLength = 4;

    private string Dmi => Path.Combine(root, "sys", "firmware", "dmi", "tables");

    private string Acpi => Path.Combine(root, "sys", "firmware", "acpi", "tables");

    public byte[]? List(uint provider) => provider == IFirmwareTables.AcpiProvider ? AcpiSignatures() : null;

    public byte[]? Read(uint provider, uint id) => provider switch
    {
        IFirmwareTables.RawSmbiosProvider => RawSmbios(),
        IFirmwareTables.AcpiProvider => AcpiTable(id),
        _ => null,
    };

    // RawSMBIOSData: a byte Windows uses for itself, the SMBIOS major and minor version, the DMI revision, the
    // table's length, then the table. The versions come from the entry point, which Linux keeps beside the table.
    private byte[]? RawSmbios()
    {
        if (TryRead(Path.Combine(Dmi, "smbios_entry_point")) is not { } entry
            || Version(entry) is not { } version
            || TryRead(Path.Combine(Dmi, "DMI")) is not { } table)
        {
            return null;
        }

        byte[] raw = new byte[RawHeaderLength + table.Length];
        raw[1] = version.Major;
        raw[2] = version.Minor;
        raw[3] = version.Revision;
        BinaryPrimitives.WriteUInt32LittleEndian(raw.AsSpan(4), (uint)table.Length);
        table.CopyTo(raw, RawHeaderLength);

        return raw;
    }

    // The 64-bit entry point of SMBIOS 3 (DSP0134 5.2.2), or the 32-bit one before it (5.2.1).
    private static (byte Major, byte Minor, byte Revision)? Version(ReadOnlySpan<byte> entry)
    {
        if (entry.Length >= 24 && entry.StartsWith("_SM3_"u8))
        {
            return (entry[7], entry[8], entry[9]);
        }

        return entry.Length >= 31 && entry.StartsWith("_SM_"u8) ? (entry[6], entry[7], entry[30]) : null;
    }

    // One file per table, named by its signature. A signature that occurs again gets a number after it, as SSDT1.
    private byte[]? AcpiSignatures()
    {
        try
        {
            List<byte> signatures = [];

            foreach (string name in Directory.EnumerateFiles(Acpi).Select(Path.GetFileName).OfType<string>().Order(StringComparer.Ordinal))
            {
                if (name.Length >= SignatureLength && Ascii.IsValid(name))
                {
                    signatures.AddRange(Encoding.ASCII.GetBytes(name[..SignatureLength]));
                }
            }

            return [.. signatures];
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    // The id is the signature's four letters read as a little-endian DWORD.
    private byte[]? AcpiTable(uint id)
    {
        Span<byte> signature = stackalloc byte[SignatureLength];
        BinaryPrimitives.WriteUInt32LittleEndian(signature, id);

        return signature.IndexOfAnyExcept("ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789_"u8) < 0
            ? TryRead(Path.Combine(Acpi, Encoding.ASCII.GetString(signature)))
            : null;
    }

    private static byte[]? TryRead(string path)
    {
        try
        {
            return File.ReadAllBytes(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
