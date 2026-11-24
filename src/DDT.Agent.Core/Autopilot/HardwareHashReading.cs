// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Agent.Autopilot;

// What Windows says about itself for Autopilot. A server edition has no serial number or hash, because none is read.
public sealed record HardwareHashReading(string ProductType, string SerialNumber, string Hash)
{
    // ProductOptions\ProductType of a client edition. Servers say ServerNT or LanmanNT.
    public const string Client = "WinNT";

    public bool IsClient => string.Equals(ProductType, Client, StringComparison.OrdinalIgnoreCase);

    // The hash runs to thousands of characters.
    public override string ToString() => $"HardwareHashReading {{ ProductType = {ProductType}, SerialNumber = {SerialNumber}, Hash = {Hash.Length} characters }}";
}
