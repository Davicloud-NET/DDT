// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Buffers.Text;
using System.Globalization;
using DDT.Agent.Deployment;

namespace DDT.Agent.Autopilot;

// Reads the hash as SYSTEM through the MDM bridge, in one PowerShell call whose output stays out of the logs. The hash
// changes a little with every collection, so the step reads it once and the server keeps that one.
public sealed class PowerShellHardwareHashReader(IToolRunner tools, string powerShellPath) : IHardwareHashReader
{
    public static readonly TimeSpan Timeout = TimeSpan.FromMinutes(5);

    private const string ProductPrefix = "product:";
    private const string SerialPrefix = "serial:";
    private const string HashPrefix = "hash:";

    // A server edition answers the MDM class too, so the edition is read first and a server ends the call.
    public static IReadOnlyList<string> Arguments() =>
    [
        "-NoProfile",
        "-NonInteractive",
        "-ExecutionPolicy",
        "Bypass",
        "-Command",
        @"try { $t = (Get-ItemProperty -Path 'HKLM:\SYSTEM\CurrentControlSet\Control\ProductOptions' -ErrorAction Stop).ProductType; "
            + $"'{ProductPrefix}' + $t; if ($t -ne '{HardwareHashReading.Client}') {{ exit 0 }}; "
            + "$d = Get-CimInstance -Namespace root/cimv2/mdm/dmmap -ClassName MDM_DevDetail_Ext01 -Filter \"InstanceID='Ext' AND ParentID='./DevDetail'\" -ErrorAction Stop; "
            + $"'{SerialPrefix}' + (Get-CimInstance -ClassName Win32_BIOS -ErrorAction Stop).SerialNumber; '{HashPrefix}' + $d.DeviceHardwareData }} "
            + "catch { [Console]::Error.WriteLine(($_ | Out-String).TrimEnd()); exit 1 }; exit 0",
    ];

    public async Task<HardwareHashReading> ReadAsync(CancellationToken cancellationToken)
    {
        PrivateOutput output = new();
        int exitCode = await tools.RunForExitCodeAsync(powerShellPath, Arguments(), new ToolRunOptions(Timeout: Timeout, Private: output), cancellationToken)
            .ConfigureAwait(false);

        if (exitCode != 0)
        {
            string error = output.Errors.FirstOrDefault(line => !string.IsNullOrWhiteSpace(line))?.Trim() ?? "no error text";

            throw new DeploymentStepException(string.Create(
                CultureInfo.InvariantCulture,
                $"PowerShell could not read the hardware hash (exit code {exitCode}): {error}"));
        }

        return Parse(output.Lines);
    }

    public static HardwareHashReading Parse(IReadOnlyList<string> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);

        string product = Value(lines, ProductPrefix)
            ?? throw new DeploymentStepException("PowerShell did not say which Windows edition this is.");

        if (!string.Equals(product, HardwareHashReading.Client, StringComparison.OrdinalIgnoreCase))
        {
            return new HardwareHashReading(product, string.Empty, string.Empty);
        }

        string serial = Value(lines, SerialPrefix) ?? string.Empty;
        string hash = Value(lines, HashPrefix) ?? string.Empty;

        if (serial.Length == 0)
        {
            throw new DeploymentStepException("Windows reports no serial number for this machine, and Autopilot needs one.");
        }

        return hash.Length > 0 && Base64.IsValid(hash)
            ? new HardwareHashReading(product, serial, hash)
            : throw new DeploymentStepException("Windows reported no hardware hash. The MDM bridge (MDM_DevDetail_Ext01) gave nothing usable.");
    }

    private static string? Value(IReadOnlyList<string> lines, string prefix) =>
        lines.FirstOrDefault(line => line.StartsWith(prefix, StringComparison.Ordinal))?[prefix.Length..].Trim();
}
