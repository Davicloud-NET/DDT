// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Runtime.InteropServices;

namespace DDT.Core.Windows;

// Which processes hold a UDP port on Windows, over IPv4. A bind can succeed there while another process takes the
// traffic, so the listeners alone do not tell who answers netboot.
public static partial class UdpPortOwners
{
    private const int AddressFamilyInet = 2;
    private const int TableOwnerProcess = 1;
    private const uint InsufficientBuffer = 122;

    // One row of MIB_UDPTABLE_OWNER_PID: the address, the port in network order, and the process
    private const int RowBytes = 12;

    // The process ids by port, for the ports asked about. Empty on another system, or when Windows does not say.
    public static IReadOnlyDictionary<int, IReadOnlyList<int>> Of(IReadOnlyCollection<int> ports)
    {
        ArgumentNullException.ThrowIfNull(ports);

        Dictionary<int, List<int>> owners = [];

        if (OperatingSystem.IsWindows() && Table() is { } table)
        {
            int rows = BitConverter.ToInt32(table, 0);

            for (int row = 0; row < rows && sizeof(int) + ((row + 1) * RowBytes) <= table.Length; row++)
            {
                int offset = sizeof(int) + (row * RowBytes);
                int port = (table[offset + 4] << 8) | table[offset + 5];
                int process = BitConverter.ToInt32(table, offset + 8);

                if (ports.Contains(port) && owners.TryAdd(port, [process]) is false && !owners[port].Contains(process))
                {
                    owners[port].Add(process);
                }
            }
        }

        return owners.ToDictionary(owner => owner.Key, owner => (IReadOnlyList<int>)owner.Value);
    }

    private static byte[]? Table()
    {
        int size = 0;
        _ = GetExtendedUdpTable(null, ref size, sort: false, AddressFamilyInet, TableOwnerProcess, 0);

        // The table can grow between the two calls
        for (int attempt = 0; attempt < 4; attempt++)
        {
            byte[] table = new byte[size];
            uint result = GetExtendedUdpTable(table, ref size, sort: false, AddressFamilyInet, TableOwnerProcess, 0);

            if (result == 0)
            {
                return table;
            }

            if (result != InsufficientBuffer)
            {
                return null;
            }
        }

        return null;
    }

    [LibraryImport("iphlpapi.dll")]
    private static partial uint GetExtendedUdpTable(byte[]? table, ref int size, [MarshalAs(UnmanagedType.Bool)] bool sort, int family, int tableClass, uint reserved);
}
