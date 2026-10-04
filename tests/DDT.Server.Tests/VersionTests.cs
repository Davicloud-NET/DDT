// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Globalization;
using System.Xml.Linq;
using DDT.Server.Data;
using Xunit;

namespace DDT.Server.Tests;

// The version is Year.Major.Build: Year and Major from Directory.Build.props, Build counted from the commits.
public sealed class VersionTests
{
    // Windows Installer takes no more in its first two numbers
    private const int Most = 255;

    [Fact]
    public void ABuildCarriesTheYearAndMajorTheRepositoryNames()
    {
        XDocument properties = XDocument.Load(Path.Combine(Repository.Root(), "Directory.Build.props"));
        int year = int.Parse(properties.Descendants("DdtVersionYear").Single().Value, CultureInfo.InvariantCulture);
        int major = int.Parse(properties.Descendants("DdtVersionMajor").Single().Value, CultureInfo.InvariantCulture);

        Assert.InRange(year, 1, Most);
        Assert.InRange(major, 1, Most);

        Version? built = typeof(DdtDbContext).Assembly.GetName().Version;
        Assert.Equal((year, major), (built?.Major, built?.Minor));
    }

    [Theory]
    [InlineData("Install-Ddt.ps1", "$released = ''")]
    [InlineData("install.sh", "\nreleased=\n")]
    [InlineData("compose.release.yaml", "ghcr.io/davicloud-net/ddt:latest")]
    public async Task TheReleaseFindsWhereItPutsItsVersion(string file, string marker)
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        string root = Repository.Root();
        string text = await File.ReadAllTextAsync(Path.Combine(root, "build", file), cancellationToken);
        string workflow = await File.ReadAllTextAsync(Path.Combine(root, ".github", "workflows", "release.yml"), cancellationToken);

        Assert.Contains(marker, text.ReplaceLineEndings("\n"), StringComparison.Ordinal);

        // As the workflow's PowerShell writes it
        Assert.Contains(marker.Replace("$", "`$", StringComparison.Ordinal).Replace("\n", "`n", StringComparison.Ordinal), workflow, StringComparison.Ordinal);
    }
}
