// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Pxe;

public sealed class PxeOptions
{
    public const string SectionName = "DDT:Pxe";

    // Comma separated interface names or IPv4 addresses, deliberately without a default. A NIC may sit on a segment
    // whose DHCP belongs to someone else, and DDT must not answer PXE there. Unset serves nothing.
    public string Interfaces { get; set; } = string.Empty;

    // Everything below it is served to anyone who asks. A relative path is inside DDT:StorePath.
    public string BootDirectory { get; set; } = "boot";

    public int HttpBootPort { get; set; } = 8080;

    // A site whose own DHCP server points next-server and bootfile at DDT needs no ProxyDHCP, and
    // turning it off means DDT binds nothing on UDP 67 or 4011.
    public bool EnableProxyDhcp { get; set; } = true;

    public bool EnableTftp { get; set; } = true;

    // Replies from port 69 instead of a new port per transfer. Use it when a read request reaches DDT but the client
    // never receives data, because a stateful firewall drops the reply.
    public bool TftpSinglePort { get; set; }

    // Caps the window, whatever the BCD asks for. Only 4 has Microsoft backing, but 16 measured reliable and about 40
    // percent faster. For a lossy link, lower this. That needs no new boot images.
    public int TftpMaxWindowSize { get; set; } = 16;

    // Comma separated rather than a list, for the same reason as DDT:Roles.
    public string AuthorisedRelayAgents { get; set; } = string.Empty;

    public int MaxConcurrentTftpTransfers { get; set; } = 128;

    // Keyed by ClientArchitecture member name as a string. The binder silently drops a dictionary key
    // it cannot convert to an enum, which would leave a machine at a blank screen with no error.
    public Dictionary<string, BootTargetOptions> BootTargets { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}
