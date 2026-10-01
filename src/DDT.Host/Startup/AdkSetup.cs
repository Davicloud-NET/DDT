// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Runtime.Versioning;
using System.Security.Cryptography;

namespace DDT.Host.Startup;

// Installs Microsoft's ADK and its Windows PE add-on, which build boot images. Each setup comes from Microsoft, has to
// match its SHA-256, and runs quietly, which accepts Microsoft's licence terms.
public sealed class AdkSetup(IAdkMachine machine, string folder, IReadOnlyList<AdkPart> parts)
{
    private const string Kit = "Assessment and Deployment Kit";

    // A pair that was tried together, not the newest: winget's ADK and add-on don't match.
    private const string Version = "10.1.26100.9457";

    public static IReadOnlyList<AdkPart> Release { get; } =
    [
        new(
            "Windows ADK",
            Version,
            "adksetup.exe",
            new Uri("https://download.microsoft.com/download/8e0c0f5a-abb5-4358-a51b-168eb40b1590/adk/adksetup.exe"),
            "AC6A930FDB5C2980BA5FEFE606D47EDAAFCF5F647B4337411500D158EA77300F",
            "OptionId.DeploymentTools",
            @"Deployment Tools\amd64\DISM\dism.exe"),
        new(
            "Windows PE add-on",
            Version,
            "adkwinpesetup.exe",
            new Uri("https://download.microsoft.com/download/a4a79e7a-f085-41c4-aebf-2538fd000790/adkwinpeaddons/adkwinpesetup.exe"),
            "D4DE67ACC83DE253CCC941DD0590808D83726BDD8EA768A374A7BEAFF7949D6A",
            "OptionId.WindowsPreinstallationEnvironment",
            @"Windows Preinstallation Environment\copype.cmd"),
    ];

    [SupportedOSPlatform("windows")]
    public static AdkSetup ForThisServer() => new(new WindowsAdkMachine(), Path.Combine(Path.GetTempPath(), "ddt-adk"), Release);

    // Returns null, or what stopped it
    public string? Install(TextWriter output)
    {
        ArgumentNullException.ThrowIfNull(output);

        AdkPart[] missing = Missing();

        if (missing.Length == 0)
        {
            output.WriteLine("The Windows ADK and its Windows PE add-on are installed already.");

            return null;
        }

        // An add-on only fits the ADK of its own version
        if (missing.Length < parts.Count && machine.AdkVersion() is { } installed && installed != missing[0].Version)
        {
            return $"This server has the Windows ADK {installed}, and DDT only gets the parts of {missing[0].Version}. " +
                $"Add the {missing[0].Name} from Microsoft's setup for {installed}.";
        }

        machine.WaitForWindowsInstaller();
        Directory.CreateDirectory(folder);

        foreach (AdkPart part in missing)
        {
            if (Install(part, output) is { } problem)
            {
                return problem;
            }
        }

        // The setups' logs, which only a failure needs
        Directory.Delete(folder, recursive: true);

        return null;
    }

    private AdkPart[] Missing() =>
        machine.KitsRoot() is { } root ? [.. parts.Where(part => !File.Exists(Path.Combine(root, Kit, part.Marker)))] : [.. parts];

    private string? Install(AdkPart part, TextWriter output)
    {
        string setup = Path.Combine(folder, part.File);
        string log = Path.ChangeExtension(setup, ".log");
        output.WriteLine($"Getting {part.File} from Microsoft.");

        try
        {
            machine.Download(part.Address, setup);
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException or TaskCanceledException)
        {
            return $"{part.File} didn't download from {part.Address.Host}: {exception.Message}";
        }

        try
        {
            if (!string.Equals(Sha256(setup), part.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                return $"{part.File} from Microsoft isn't the file this release of DDT was tried with, so it didn't run.";
            }

            output.WriteLine($"Installing the {part.Name} {part.Version}, which takes a few minutes.");
            int exit = machine.Run(setup, ["/quiet", "/norestart", "/ceip", "off", "/features", part.Feature, "/log", log]).ExitCode;

            // 3010: done, and Windows wants a restart
            if (exit is not (0 or 3010) || Missing().Contains(part))
            {
                return $"{part.File} ended with exit code {exit}, without the {part.Name}. Its log is {log}.";
            }
        }
        finally
        {
            File.Delete(setup);
        }

        output.WriteLine($"Installed the {part.Name}.");

        return null;
    }

    private static string Sha256(string path)
    {
        using FileStream file = File.OpenRead(path);

        return Convert.ToHexString(SHA256.HashData(file));
    }
}
