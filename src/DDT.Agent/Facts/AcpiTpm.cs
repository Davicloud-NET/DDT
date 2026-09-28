// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Agent.Facts;

// Which TPM the machine has, from the ACPI table that describes it: TPM2 for a TPM 2.0, firmware TPMs included, and
// TCPA for a TPM 1.2. WinPE has no TPM Base Services, but the ACPI tables are there in both phases.
public static class AcpiTpm
{
    public const string Version20 = "2.0";
    public const string Version12 = "1.2";

    // Null for none. tableIds is EnumSystemFirmwareTables' ACPI list of four-letter table signatures. Read as a
    // little-endian DWORD, each is also GetSystemFirmwareTable's id. Firmware that lists both describes a TPM 2.0.
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
