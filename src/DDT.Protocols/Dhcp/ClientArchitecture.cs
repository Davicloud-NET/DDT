// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Protocols.Dhcp;

// The IANA "Processor Architecture Types" registry for DHCP option 93, complete as published. Deprecated entries stay,
// because a client may still send them and a name reads better in a log than a number.
public enum ClientArchitecture : ushort
{
    X86Bios = 0x00,
    Nec98 = 0x01,
    Itanium = 0x02,
    DecAlpha = 0x03,
    ArcX86 = 0x04,
    IntelLeanClient = 0x05,
    X86Uefi = 0x06,
    X64Uefi = 0x07,
    EfiXscale = 0x08,
    Ebc = 0x09,
    Arm32Uefi = 0x0A,
    Arm64Uefi = 0x0B,
    PowerPcOpenFirmware = 0x0C,
    PowerPcEpapr = 0x0D,
    PowerOpalV3 = 0x0E,
    X86UefiHttp = 0x0F,
    X64UefiHttp = 0x10,
    EbcHttp = 0x11,
    Arm32UefiHttp = 0x12,
    Arm64UefiHttp = 0x13,
    PcAtBiosHttp = 0x14,
    Arm32Uboot = 0x15,
    Arm64Uboot = 0x16,
    Arm32UbootHttp = 0x17,
    Arm64UbootHttp = 0x18,
    RiscV32Uefi = 0x19,
    RiscV32UefiHttp = 0x1A,
    RiscV64Uefi = 0x1B,
    RiscV64UefiHttp = 0x1C,
    RiscV128Uefi = 0x1D,
    RiscV128UefiHttp = 0x1E,
    S390Basic = 0x1F,
    S390Extended = 0x20,
    Mips32Uefi = 0x21,
    Mips64Uefi = 0x22,
    Sunway32Uefi = 0x23,
    Sunway64Uefi = 0x24,
    LoongArch32Uefi = 0x25,
    LoongArch32UefiHttp = 0x26,
    LoongArch64Uefi = 0x27,
    LoongArch64UefiHttp = 0x28,
    ArmRpiBoot = 0x29,
}
