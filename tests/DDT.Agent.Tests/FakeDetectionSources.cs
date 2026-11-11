// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Agent.Applications;

namespace DDT.Agent.Tests;

// The machine a detection rule looks at: the products, the uninstall keys, the files with their versions (null for a
// file without one), and the provisioned packages' full names. Variables expands %ProgramFiles% only.
internal sealed class FakeDetectionSources : IInstalledProducts, IUninstallKeys, IDetectionFiles, IProvisionedPackages
{
    public List<string> Provisioned { get; } = [];

    public HashSet<string> Products { get; } = new(StringComparer.OrdinalIgnoreCase);

    public HashSet<string> UninstallKeys { get; } = new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, string?> Files { get; } = new(StringComparer.OrdinalIgnoreCase);

    public bool IsInstalled(string productCode) => Products.Contains(productCode);

    bool IUninstallKeys.Exists(string keyName) => UninstallKeys.Contains(keyName);

    public string Expand(string path) => path.Replace("%ProgramFiles%", @"C:\Program Files", StringComparison.OrdinalIgnoreCase);

    bool IDetectionFiles.Exists(string path) => Files.ContainsKey(path);

    public string? VersionOf(string path) => Files[path];

    public IReadOnlyList<string> FullNames() => Provisioned;
}
