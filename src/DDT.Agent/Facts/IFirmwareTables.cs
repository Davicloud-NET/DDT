// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Agent.Facts;

// The tables the firmware hands to Windows, such as the SMBIOS structure table and the ACPI tables, as
// EnumSystemFirmwareTables and GetSystemFirmwareTable give them. Providers and table ids are the DWORDs those take.
public interface IFirmwareTables
{
    // The provider's table ids, four bytes each, as EnumSystemFirmwareTables writes them; null when Windows cannot list
    // them. For ACPI these are the tables' signatures with their letters in order, such as "TPM2".
    byte[]? List(uint provider);

    // The table as GetSystemFirmwareTable writes it; null when there is none or Windows cannot read it.
    byte[]? Read(uint provider, uint id);
}
