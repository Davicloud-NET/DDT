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
    // How long a look at the files counts. The handler answers every PXE request on the segment.
    private static readonly TimeSpan s_fresh = TimeSpan.FromSeconds(5);

    private readonly PxeSetup _setup;
    private readonly IBootChoice? _choice;
    private readonly BootFiles _files;
    private readonly TimeProvider _timeProvider;
    private Availability? _available;

    public NetbootChoice(PxeSetup setup, IBootChoice? choice, BootFiles files, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(setup);
        ArgumentNullException.ThrowIfNull(files);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _setup = setup;
        _choice = choice;
        _files = files;
        _timeProvider = timeProvider;
    }

    // Null when the server can start no boot environment for this machine, so it gets no answer.
    public ProxyDhcpConfiguration? For(DhcpMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);

        // Only this architecture loads shim over TFTP. HTTP Boot firmware and other processors keep their targets.
        if (message.Architecture != ClientArchitecture.X64Uefi)
        {
            return _setup.ProxyDhcp;
        }

        Availability available = Available();

        return BootEnvironmentChoice.Decide(Wish(message), _setup.DefaultEnvironment, available.WindowsPE, available.Linux) switch
        {
            AgentEnvironment.LinuxBoot => _setup.LinuxProxyDhcp,
            AgentEnvironment.WindowsPE => _setup.ProxyDhcp,
            _ => null,
        };
    }

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

    private Availability Available()
    {
        Availability? known = Volatile.Read(ref _available);
        DateTimeOffset now = _timeProvider.GetUtcNow();

        if (known is null || now - known.At > s_fresh || now < known.At)
        {
            known = new Availability(now, WindowsPEStarts(), _setup.LinuxProxyDhcp is not null && _files.LinuxAvailable);
            Volatile.Write(ref _available, known);
        }

        return known;
    }

    // The boot file of the settings is there, or another TFTP server is named for it, which DDT cannot look into.
    private bool WindowsPEStarts() =>
        _setup.ProxyDhcp.BootTargets.TryGetValue(ClientArchitecture.X64Uefi, out BootTarget? target)
        && (target.ServerAddress is not null || _files.Boot.TryResolve(target.BootFile, out _));

    private sealed record Availability(DateTimeOffset At, bool WindowsPE, bool Linux);
}
