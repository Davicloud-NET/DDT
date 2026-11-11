// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Server.Applications.Installers;

// A section of a PE file: where it is loaded and where its bytes are in the file.
internal sealed record PeSection(string Name, uint VirtualAddress, uint VirtualSize, long RawOffset, long RawLength)
{
    public bool Contains(uint rva) => rva >= VirtualAddress && rva - VirtualAddress < Math.Max(VirtualSize, (uint)RawLength);
}
