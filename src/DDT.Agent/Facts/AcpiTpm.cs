// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Agent.Facts;

// Which TPM the machine has, from the ACPI table its firmware describes the TPM in: TPM2 for a TPM 2.0, which firmware
// TPMs publish too, and TCPA for a TPM 1.2. Windows PE has no TPM Base Services, and the ACPI tables are there in both
// phases, through the same function as the SMBIOS table.
public static class AcpiTpm
{
    public const string Version20 = "2.0";
    public const string Version12 = "1.2";

    // The version of the TPM the tables describe, or null for none. tableIds is what EnumSystemFirmwareTables writes for
    // the ACPI provider: every table's four letter signature, its letters in order, which read as a little-endian
    // DWORD is also the id GetSystemFirmwareTable takes. Firmware that lists both describes a TPM 2.0.
    public static string? Version(ReadOnlySpan<byte> tableIds)
    {
        bool tpm12 = false;

        for (int offset = 0; offset + 4 <= tableIds.Length; offset += 4)
        {
            ReadOnlySpan<byte> signature = tableIds.Slice(offset, 4);

            if (signature.SequenceEqual("TPM2"u8))
            {
                return Version20;
            }

            tpm12 |= signature.SequenceEqual("TCPA"u8);
        }

        return tpm12 ? Version12 : null;
    }
}
