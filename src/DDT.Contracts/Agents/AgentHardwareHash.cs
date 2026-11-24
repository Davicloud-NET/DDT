// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Contracts.Agents;

// The Autopilot hardware hash, base64 as Windows reports it, from a Register with Autopilot step.
public sealed record AgentHardwareHash(string SerialNumber, string Hash, string? GroupTag) : AgentRecord
{
    // The hash runs to thousands of characters.
    public override string ToString() => $"AgentHardwareHash {{ SerialNumber = {SerialNumber}, Hash = {Hash.Length} characters, GroupTag = {GroupTag} }}";
}
