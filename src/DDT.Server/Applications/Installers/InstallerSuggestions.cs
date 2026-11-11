// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Server.Applications.Installers;

// The silent switches and exit codes each installer kind documents.
public static class InstallerSuggestions
{
    private const int Success = 0;
    private const int RestartRequired = 3010;
    private const int RestartStarted = 1641;

    private static readonly Dictionary<InstallerKind, Convention> s_conventions = new()
    {
        [InstallerKind.Msi] = new(true, "/qn /norestart", [Success], [RestartRequired, RestartStarted]),
        [InstallerKind.Burn] = new(false, "/quiet /norestart", [Success], [RestartRequired]),

        // Inno Setup returns 0 after a successful install that needs a restart unless /RESTARTEXITCODE says otherwise.
        [InstallerKind.InnoSetup] = new(false, "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /RESTARTEXITCODE=3010", [Success], [RestartRequired]),
        [InstallerKind.Nsis] = new(false, "/S", [Success], []),

        // Covers Basic MSI projects; an InstallScript project needs a recorded response file instead.
        [InstallerKind.InstallShield] = new(false, "/s /v\"/qn /norestart\"", [Success], [RestartRequired]),
    };

    // Returns null for an unknown kind, or a file name a command line can't quote.
    public static InstallerSuggestion? For(InstallerFacts facts, string fileName)
    {
        ArgumentNullException.ThrowIfNull(facts);
        ArgumentNullException.ThrowIfNull(fileName);

        if (!s_conventions.TryGetValue(facts.Kind, out Convention? convention) || fileName.Length == 0 || fileName.Contains('"', StringComparison.Ordinal))
        {
            return null;
        }

        string command = convention.ViaMsiexec ? $"msiexec /i \"{fileName}\" {convention.Arguments}" : $"\"{fileName}\" {convention.Arguments}";
        string? productCode = facts.Kind == InstallerKind.Msi ? Code(facts.ProductCode) : null;
        string? uninstallKey = facts.Kind == InstallerKind.Burn ? Code(facts.BundleCode) : null;

        return new InstallerSuggestion(command, [.. convention.SuccessExitCodes], [.. convention.RestartExitCodes], productCode, uninstallKey);
    }

    // Windows Installer and Burn write their codes as upper-case GUIDs in braces.
    private static string? Code(string? value) =>
        Guid.TryParse(value, out Guid code) ? code.ToString("B").ToUpperInvariant() : null;

    private sealed record Convention(bool ViaMsiexec, string Arguments, int[] SuccessExitCodes, int[] RestartExitCodes);
}
