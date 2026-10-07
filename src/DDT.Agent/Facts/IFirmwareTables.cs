// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Agent.Facts;

// The firmware's tables, such as the SMBIOS structure table and the ACPI tables, in the layout Windows gives them
// with EnumSystemFirmwareTables and GetSystemFirmwareTable. Providers and table ids are the DWORDs those take.
public interface IFirmwareTables
{
    // The provider's table ids, four bytes each. Null when they can't be listed. For ACPI these are the tables'
    // signatures with their letters in order, such as "TPM2".
    byte[]? List(uint provider);

    // The table. For RSMB that is RawSMBIOSData: four bytes of versions, the table's length as a DWORD, then the
    // structure table. Null when there's none or it can't be read.
    byte[]? Read(uint provider, uint id);
}
