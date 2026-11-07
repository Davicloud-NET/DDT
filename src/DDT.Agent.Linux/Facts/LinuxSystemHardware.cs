// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Globalization;

namespace DDT.Agent.Facts;

// The machine's memory and processors as Linux shows them under /sys and /proc.
public sealed class LinuxSystemHardware(string root) : ISystemHardware
{
    private const string MemoryTotal = "MemTotal:";
    private const string ProcessorPrefix = "cpu";

    // What the SMBIOS memory devices add up to, which is the memory on the machine's label.
    public ulong? InstalledMemoryKilobytes()
    {
        try
        {
            ulong total = SmbiosMemoryDevices.TotalKilobytes(File.ReadAllBytes(Path.Combine(root, "sys", "firmware", "dmi", "tables", "DMI")));

            return total > 0 ? total : null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    // The line reads "MemTotal:       16303412 kB".
    public ulong? UsableMemoryBytes()
    {
        try
        {
            foreach (string line in File.ReadLines(Path.Combine(root, "proc", "meminfo")))
            {
                if (line.StartsWith(MemoryTotal, StringComparison.Ordinal))
                {
                    string[] parts = line[MemoryTotal.Length..].Split(' ', StringSplitOptions.RemoveEmptyEntries);

                    return parts.Length > 0 && ulong.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out ulong kilobytes) && kilobytes > 0
                        ? kilobytes * 1024
                        : null;
                }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }

        return null;
    }

    // A core is one core id within one package. A processor that is switched off has no topology and is not counted.
    public ProcessorCount? ProcessorCores()
    {
        try
        {
            HashSet<(string Package, string Core)> cores = [];
            int logical = 0;

            foreach (string processor in Directory.EnumerateDirectories(Path.Combine(root, "sys", "devices", "system", "cpu"), ProcessorPrefix + "*"))
            {
                string topology = Path.Combine(processor, "topology");
                string number = Path.GetFileName(processor)[ProcessorPrefix.Length..];

                // The folder also holds cpufreq and cpuidle.
                if (number.Length > 0 && number.All(char.IsAsciiDigit) && SysfsText.Read(Path.Combine(topology, "core_id")) is { } core)
                {
                    logical++;
                    cores.Add((SysfsText.Read(Path.Combine(topology, "physical_package_id")) ?? "0", core));
                }
            }

            return logical > 0 ? new ProcessorCount(cores.Count, logical) : null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
