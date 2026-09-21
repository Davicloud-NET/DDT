// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Net;
using DDT.Protocols.Dhcp;

namespace DDT.Protocols.Pxe;

public sealed record BootTarget
{
    public required ClientArchitecture Architecture { get; init; }

    public required BootMethod Method { get; init; }

    // A TFTP path, or an absolute URL when Method is Http.
    public required string BootFile { get; init; }

    // Null means use the address of the interface the request arrived on, which is the only correct
    // answer when one host serves several segments.
    public IPAddress? ServerAddress { get; init; }

    public string? ServerHostName { get; init; }

    // Emits option 43 with PXE_DISCOVERY_CONTROL. Only ever useful for legacy BIOS clients: UEFI
    // firmware classifies an offer carrying option 43 differently and can refuse it.
    public bool AdvertiseBootServerDiscovery { get; init; }
}
