// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Agent.Facts;

namespace DDT.Agent.Tests;

// Firmware tables in memory: Lists holds what EnumSystemFirmwareTables writes per provider, Tables each table. Every call
// throws Failure when it is set.
internal sealed class FakeFirmwareTables : IFirmwareTables
{
    public Dictionary<uint, byte[]> Lists { get; } = [];

    public Dictionary<(uint Provider, uint Id), byte[]> Tables { get; } = [];

    public Exception? Failure { get; set; }

    public byte[]? List(uint provider)
    {
        ThrowIfFailing();

        return Lists.GetValueOrDefault(provider);
    }

    public byte[]? Read(uint provider, uint id)
    {
        ThrowIfFailing();

        return Tables.GetValueOrDefault((provider, id));
    }

    private void ThrowIfFailing()
    {
        if (Failure is { } failure)
        {
            throw failure;
        }
    }
}
