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

    // Leaves UDP 67 to a DHCP server on this computer, and answers on UDP 4011 alone. Machines come there once that DHCP
    // server sends option 60, PXEClient. Where Microsoft's DHCP Server is installed, DDT does so by itself.
    public bool LeaveDhcpPort { get; set; }

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

    // What a machine without a run starts: Auto, WindowsPE or Linux. Auto is Windows PE once a boot image is built,
    // and the Linux boot environment until then. A string, so a wrong value is named instead of becoming Auto.
    public string DefaultBootEnvironment { get; set; } = "Auto";

    // Words added to the kernel command line of the Linux boot environment, such as console=ttyS0,115200.
    // KernelArguments says which are allowed, because they go into grub.cfg.
    public string LinuxKernelArguments { get; set; } = string.Empty;

    // Keyed by ClientArchitecture member name as a string. The binder silently drops a dictionary key
    // it cannot convert to an enum, which would leave a machine at a blank screen with no error.
    public Dictionary<string, BootTargetOptions> BootTargets { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}
