// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Rules;
using DDT.Server.Machines;

namespace DDT.Server.Rules;

public static class AssignmentRuleKeys
{
    public const int MaxDescriptionLength = 256;

    // "model:" and the separator around the manufacturer and the model at their longest.
    public const int MaxMatchKeyLength = 300;

    // One column keeps the rule unique on every database: mac:001122AABBCC, or model:MANUFACTURER|MODEL with an
    // empty manufacturer for any.
    public static string MatchKey(AssignmentRuleKind kind, string? mac, string? manufacturer, string? model) =>
        kind == AssignmentRuleKind.Mac
            ? $"mac:{mac}"
            : $"model:{HardwareModels.Normalize(manufacturer)}|{HardwareModels.Normalize(model)}";

    // For people: MAC address 00:15:5D:01:02:03, model Dell Inc. Latitude 5440, or model Latitude 7* of any maker.
    public static string Describe(AssignmentRule rule)
    {
        ArgumentNullException.ThrowIfNull(rule);

        if (rule.Kind == AssignmentRuleKind.Mac)
        {
            return $"MAC address {string.Join(':', (rule.Mac ?? "").Chunk(2).Select(pair => new string(pair)))}";
        }

        return rule.Manufacturer is { } manufacturer ? $"model {manufacturer} {rule.Model}" : $"model {rule.Model} of any maker";
    }
}
