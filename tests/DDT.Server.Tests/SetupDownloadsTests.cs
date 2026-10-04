// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Text.RegularExpressions;
using Xunit;

namespace DDT.Server.Tests;

// The setup pages and install.ps1 each name Microsoft's IIS modules, by address and SHA-256.
public sealed partial class SetupDownloadsTests
{
    [Fact]
    public async Task TheSetupPagesAndTheInstallScriptGetTheSameIisModules()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        string root = Repository.Root();
        string pages = await File.ReadAllTextAsync(Path.Combine(root, "build", "Installer", "CustomActions", "Dependencies.cs"), cancellationToken);
        string script = await File.ReadAllTextAsync(Path.Combine(root, "build", "Install-Ddt.ps1"), cancellationToken);

        string[] named = [.. Pinned().Matches(pages).Select(match => match.Value).Order(StringComparer.Ordinal)];

        Assert.Equal(4, named.Length);
        Assert.Equal(named, Pinned().Matches(script).Select(match => match.Value).Order(StringComparer.Ordinal));
    }

    [GeneratedRegex(@"https://download\.microsoft\.com/[^""'\s]+|\b[0-9A-F]{64}\b")]
    private static partial Regex Pinned();
}
