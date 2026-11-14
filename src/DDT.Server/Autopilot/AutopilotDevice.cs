// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Server.Autopilot;

// One device to import: the hash base64 as Windows reports it.
public sealed record AutopilotDevice(string SerialNumber, string Hash, string? GroupTag)
{
    // The hash runs to thousands of characters.
    public override string ToString() => $"AutopilotDevice {{ SerialNumber = {SerialNumber}, Hash = {Hash.Length} characters, GroupTag = {GroupTag} }}";
}
