// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Net;

namespace DDT.Pxe;

// What a generated grub.cfg carries. Server is the address the machine asked on. A null HttpPort leaves the HTTP lines
// out. HostName is the server's name when its address is not one, for the machine's hosts file.
public sealed record GrubConfigValues(IPAddress Server, int? HttpPort, DateTimeOffset Now)
{
    public string Keymap { get; init; } = GrubConfig.DefaultKeymap;

    public string? HostName { get; init; }

    public string? KernelArguments { get; init; }
}
