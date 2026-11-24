// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Text;

namespace DDT.Server.Autopilot;

// The file Intune imports by hand: its own header, no quotes, ANSI text, at most 500 devices.
// https://learn.microsoft.com/autopilot/add-devices#ensure-that-the-csv-file-meets-requirements
public static class AutopilotCsv
{
    public const string Header = "Device Serial Number,Windows Product ID,Hardware Hash,Group Tag,Assigned User";

    public const int MaxDevices = 500;

    public const string ContentType = "text/csv";

    public static byte[] Write(IEnumerable<AutopilotDevice> devices)
    {
        ArgumentNullException.ThrowIfNull(devices);

        StringBuilder csv = new(Header);
        csv.Append("\r\n");

        foreach (AutopilotDevice device in devices)
        {
            csv.Append(Field(device.SerialNumber)).Append(",,").Append(device.Hash).Append(',').Append(Field(device.GroupTag)).Append(",\r\n");
        }

        return Encoding.ASCII.GetBytes(csv.ToString());
    }

    // A comma or a quote would break the row, and Intune reads no quoting.
    private static string Field(string? value) =>
        new([.. (value ?? string.Empty).Where(c => c is >= ' ' and <= '~' and not (',' or '"'))]);
}
