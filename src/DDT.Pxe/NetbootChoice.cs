// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Agents;
using DDT.Protocols.Dhcp;
using DDT.Protocols.Pxe;

namespace DDT.Pxe;

// Picks what a PXE request of a 64-bit UEFI machine is answered with: the Windows PE boot target or the Linux boot
// environment's shim. Every other request keeps the boot targets of the settings.
public sealed class NetbootChoice
{
    private readonly PxeSetup _setup;
    private readonly IBootChoice? _choice;
    private readonly BootFiles _files;

    // Until the files were looked at, Windows PE counts as there: the answer of the settings alone.
    private volatile bool _windowsPE = true;

    public NetbootChoice(PxeSetup setup, IBootChoice? choice, BootFiles files)
    {
        ArgumentNullException.ThrowIfNull(setup);
        ArgumentNullException.ThrowIfNull(files);

        _setup = setup;
        _choice = choice;
        _files = files;
    }

    // Null when no boot environment can be started for this machine. Answers from memory: the DHCP loop calls it.
    public ProxyDhcpConfiguration? For(DhcpMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);

        // Only this architecture loads shim over TFTP. HTTP Boot firmware and other processors keep their targets.
        if (message.Architecture != ClientArchitecture.X64Uefi)
        {
            return _setup.ProxyDhcp;
        }

        bool linux = _setup.LinuxProxyDhcp is not null && _files.LinuxAvailable;

        return BootEnvironmentChoice.Decide(Wish(message), _setup.DefaultEnvironment, _windowsPE, linux) switch
        {
            AgentEnvironment.LinuxBoot => _setup.LinuxProxyDhcp,
            AgentEnvironment.WindowsPE => _setup.ProxyDhcp,
            _ => null,
        };
    }

    // What the last look found: whether Windows PE can be started, which is what Auto decides by.
    public bool CanStartWindowsPE => _windowsPE;

    // Looks whether Windows PE can be started, which reads the boot directory. Netboot calls it in the background.
    public void Refresh() => _windowsPE = WindowsPEStarts();

    private BootWish Wish(DhcpMessage message)
    {
        if (_choice is null)
        {
            return BootWish.None;
        }

        // Option 97 is a type octet, zero for a UUID, and sixteen octets.
        ReadOnlyMemory<byte> identifier = message.MachineIdentifier;
        ReadOnlyMemory<byte> uuid = identifier.Length == 17 && identifier.Span[0] == 0 ? identifier[1..] : ReadOnlyMemory<byte>.Empty;

        try
        {
            return _choice.For(new NetbootMachine(Convert.ToHexString(message.ClientHardwareAddress.Span), uuid));
        }
        catch (Exception)
        {
            // Whatever fails in the server's list, the machine still gets the default instead of no answer.
            return BootWish.None;
        }
    }

    // The boot file of the settings is there, or another TFTP server is named for it, which DDT cannot look into.
    private bool WindowsPEStarts() =>
        _setup.ProxyDhcp.BootTargets.TryGetValue(ClientArchitecture.X64Uefi, out BootTarget? target)
        && (target.ServerAddress is not null || _files.Boot.TryResolve(target.BootFile, out _));
}
