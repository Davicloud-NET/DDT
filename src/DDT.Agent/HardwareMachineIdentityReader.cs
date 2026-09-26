// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace DDT.Agent;

public sealed class HardwareMachineIdentityReader : IMachineIdentityReader
{
    public MachineIdentity Read()
    {
        SmbiosSystemInformation? system = ReadSmbios();
        (string primary, List<string> macs) = ReadMacAddresses();

        return new MachineIdentity(
            (system?.Uuid ?? Guid.Empty).ToString("D"),
            primary,
            macs,
            system?.Manufacturer,
            system?.ProductName,
            system?.SerialNumber,
            SecureBootState.Read(),
            SecureBootTrust.Read());
    }

    private static SmbiosSystemInformation? ReadSmbios()
    {
        uint size = NativeMethods.GetSystemFirmwareTable(NativeMethods.RawSmbiosProvider, 0, null, 0);

        if (size == 0)
        {
            return null;
        }

        byte[] buffer = new byte[size];
        uint written = NativeMethods.GetSystemFirmwareTable(NativeMethods.RawSmbiosProvider, 0, buffer, size);

        return written == 0 ? null : SmbiosParser.TryReadSystemInformation(buffer.AsSpan(0, (int)Math.Min(written, size)));
    }

    // The primary MAC is the adapter that carries the default route, which is the one the server sees
    // and the one PXE booted from. Adapters without link still count, because they are part of the
    // machine a technician will recognise.
    private static (string Primary, List<string> All) ReadMacAddresses()
    {
        List<(string Mac, bool Primary)> adapters = [];

        foreach (NetworkInterface adapter in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (adapter.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel or NetworkInterfaceType.Ppp)
            {
                continue;
            }

            byte[] address = adapter.GetPhysicalAddress().GetAddressBytes();

            if (address.Length != 6 || address.All(value => value == 0))
            {
                continue;
            }

            bool hasGateway = adapter.OperationalStatus == OperationalStatus.Up
                && adapter.GetIPProperties().GatewayAddresses.Any(gateway =>
                    gateway.Address.AddressFamily == AddressFamily.InterNetwork
                    && !gateway.Address.Equals(System.Net.IPAddress.Any));

            adapters.Add((Convert.ToHexString(address), hasGateway));
        }

        List<string> sorted = [.. adapters.Select(adapter => adapter.Mac).Distinct().Order(StringComparer.Ordinal)];

        if (sorted.Count == 0)
        {
            throw new InvalidOperationException("No network adapter with a MAC address was found.");
        }

        string primary = adapters.FirstOrDefault(adapter => adapter.Primary).Mac ?? sorted[0];

        // The server accepts at most this many, and a host with many virtual adapters has more. The
        // primary is always kept.
        List<string> all = [primary, .. sorted.Where(mac => mac != primary).Take(AgentLimits.MaxMacAddresses - 1)];

        return (primary, all);
    }
}
