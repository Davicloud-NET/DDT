// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Protocols.Dhcp;

public static class DhcpOption
{
    public const byte Pad = 0;
    public const byte VendorSpecific = 43;
    public const byte Overload = 52;
    public const byte MessageType = 53;
    public const byte ServerIdentifier = 54;
    public const byte ParameterRequestList = 55;
    public const byte MaximumMessageSize = 57;
    public const byte VendorClassIdentifier = 60;
    public const byte ClientIdentifier = 61;
    public const byte TftpServerName = 66;
    public const byte BootFileName = 67;
    public const byte ClientArchitecture = 93;
    public const byte ClientNetworkInterface = 94;
    public const byte ClientMachineIdentifier = 97;
    public const byte End = 255;
}
