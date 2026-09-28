// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Text.Json;
using DDT.Contracts;
using DDT.Contracts.Machines;

namespace DDT.Server.Machines;

// The stored form of a machine's facts is the contract record as DdtJsonContext writes it.
public static class MachineFactsDocuments
{
    public static string Write(MachineFacts facts) => JsonSerializer.Serialize(facts, DdtJsonContext.Default.MachineFacts);

    // Facts another build wrote that no longer parse read as none: the next registration writes them again.
    public static MachineFacts? Read(string? facts)
    {
        if (facts is null)
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize(facts, DdtJsonContext.Default.MachineFacts);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
