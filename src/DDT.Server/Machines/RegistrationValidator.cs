// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Globalization;
using DDT.Contracts.Agents;

namespace DDT.Server.Machines;

// Everything here was typed by whoever booted boot.wim, so it is bounded and normalised before it
// reaches the database or the web UI.
public static class RegistrationValidator
{
    private const int MaxMacAddresses = 16;
    private const int MaxTextLength = 128;
    private const int MaxVersionLength = 32;
    private const int MaxDisks = 16;
    private const int MaxDiskModelLength = 64;
    private const int MaxBusTypeLength = 16;

    // The column's length. PostgreSQL refuses a longer value, and the agent would resend it forever.
    internal const int MaxDisksLength = 512;

    private const double BytesPerGigabyte = 1024d * 1024 * 1024;

    public static bool TryNormalise(
        AgentRegistration registration,
        out NormalisedRegistration? normalised,
        out string error)
    {
        ArgumentNullException.ThrowIfNull(registration);

        normalised = null;

        if (!Guid.TryParse(registration.SmbiosUuid, out Guid uuid))
        {
            error = "smbiosUuid is not a UUID.";

            return false;
        }

        if (registration.MacAddresses is null || registration.MacAddresses.Count is 0 or > MaxMacAddresses)
        {
            error = $"macAddresses must hold between 1 and {MaxMacAddresses} addresses.";

            return false;
        }

        List<string> macs = [];

        foreach (string mac in registration.MacAddresses)
        {
            if (NormaliseMac(mac) is not { } parsed)
            {
                error = "macAddresses contains a value that is not a MAC address.";

                return false;
            }

            if (!macs.Contains(parsed))
            {
                macs.Add(parsed);
            }
        }

        if (NormaliseMac(registration.PrimaryMac) is not { } primary || !macs.Contains(primary))
        {
            error = "primaryMac must be one of macAddresses.";

            return false;
        }

        AgentDisk[]? disks = registration.Disks is null ? null : [.. registration.Disks.OfType<AgentDisk>()];

        normalised = new NormalisedRegistration(
            uuid.ToString("D"),
            primary,
            macs,
            Bound(registration.Manufacturer, MaxTextLength),
            Bound(registration.Model, MaxTextLength),
            Bound(registration.SerialNumber, MaxTextLength),
            Bound(registration.AgentVersion, MaxVersionLength) ?? "unknown",
            registration.ResumeToken,
            disks is null ? null : DescribeDisks(disks),
            disks?.Length);
        error = string.Empty;

        return true;
    }

    // One line per disk, whole lines only, so the list stays readable when it has to be cut short. The count
    // stays exact: it decides whether a web assignment can pick the disk by itself.
    private static string? DescribeDisks(AgentDisk[] disks)
    {
        List<string> lines = [];
        int length = 0;

        foreach (AgentDisk disk in disks.Take(MaxDisks))
        {
            string line = string.Create(
                CultureInfo.InvariantCulture,
                $"Disk {disk.Number}: {DiskText(disk.Model, MaxDiskModelLength) ?? "unknown model"}, {Size(disk.SizeBytes)}, {DiskText(disk.BusType, MaxBusTypeLength) ?? "unknown bus"}");
            int added = line.Length + (lines.Count > 0 ? 1 : 0);

            if (length + added > MaxDisksLength)
            {
                break;
            }

            lines.Add(line);
            length += added;
        }

        return lines.Count > 0 ? string.Join('\n', lines) : null;
    }

    // Binary gigabytes, labelled the way Windows labels them, so a 64 GB virtual disk reads 64 GB.
    private static string Size(long bytes)
    {
        double gigabytes = Math.Max(bytes, 0) / BytesPerGigabyte;

        return gigabytes >= 1024
            ? string.Create(CultureInfo.InvariantCulture, $"{gigabytes / 1024:0.#} TB")
            : string.Create(CultureInfo.InvariantCulture, $"{gigabytes:0} GB");
    }

    internal static string? NormaliseMac(string? value)
    {
        if (value is null)
        {
            return null;
        }

        string hex = value.Replace(":", string.Empty, StringComparison.Ordinal)
            .Replace("-", string.Empty, StringComparison.Ordinal);

        return hex.Length == 12 && ulong.TryParse(hex, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out _)
            ? hex.ToUpperInvariant()
            : null;
    }

    // PostgreSQL text cannot hold a NUL, and a line break would split one disk's line in two.
    private static string? DiskText(string? value, int maxLength) =>
        Bound(value is null ? null : new string([.. value.Where(c => !char.IsControl(c))]), maxLength);

    private static string? Bound(string? value, int maxLength)
    {
        string? trimmed = value?.Trim();

        if (string.IsNullOrEmpty(trimmed))
        {
            return null;
        }

        return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength];
    }
}
