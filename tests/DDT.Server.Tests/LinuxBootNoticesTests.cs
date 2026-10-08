// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Text.Json;
using System.Text.RegularExpressions;
using Xunit;

namespace DDT.Server.Tests;

// The Linux boot environment is made of the Debian packages in build/LinuxBoot/packages.lock.json. Each must be
// named in THIRD-PARTY-NOTICES.md, have its copyright file in licenses/linux-boot, and have its source listed in the
// lock for build/LinuxBoot/sources.sh. So a lock that moved can't ship without its notices and its sources.
public sealed partial class LinuxBootNoticesTests
{
    // The vendors' firmware has no source code, so a release leaves its upstream tarball out. Nothing else may be.
    private const string BinaryOnlyPool = "pool/non-free-firmware/";

    [Fact]
    public async Task TheNoticesNameEveryPackageAndEverySourceOfTheLock()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        string root = Repository.Root();
        string notices = await File.ReadAllTextAsync(Path.Combine(root, "THIRD-PARTY-NOTICES.md"), cancellationToken);
        using JsonDocument lockFile = await ReadLockAsync(root, cancellationToken);

        List<string> names = [.. Packages(lockFile).Select(Name), .. Sources(lockFile).Select(Name)];

        Assert.Contains("shim-signed", names);
        Assert.Contains("grub2", names);
        IEnumerable<string> unnamed = names.Distinct().Where(name => !notices.Contains($"`{name}`", StringComparison.Ordinal));
        Assert.Empty(unnamed.Order(StringComparer.Ordinal));

        IEnumerable<string> unlinked = Sources(lockFile).Select(source => CopyrightFile(Name(source)))
            .Where(file => !notices.Contains(file, StringComparison.Ordinal));
        Assert.Empty(unlinked.Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task TheLicencesFolderHoldsTheCopyrightFileOfEverySourceAndNoOther()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        string root = Repository.Root();
        using JsonDocument lockFile = await ReadLockAsync(root, cancellationToken);

        List<string> expected = [.. Sources(lockFile).Select(source => Name(source) + ".copyright").Distinct()];
        IEnumerable<string> found = Directory.EnumerateFiles(Path.Combine(root, "licenses", "linux-boot"), "*.copyright")
            .Select(file => Path.GetFileName(file));

        Assert.Equal(expected.Order(StringComparer.Ordinal), found.Order(StringComparer.Ordinal));
        Assert.All(Packages(lockFile), package => Assert.Contains(Text(package, "source") + ".copyright", expected));
    }

    // Debian's copyright files point to /usr/share/common-licenses for the long licences instead of quoting them.
    [Fact]
    public async Task TheLicencesFolderHoldsEveryLicenceTextACopyrightFilePointsTo()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        string folder = Path.Combine(Repository.Root(), "licenses", "linux-boot");
        SortedSet<string> pointedTo = new(StringComparer.Ordinal);

        foreach (string file in Directory.EnumerateFiles(folder, "*.copyright"))
        {
            string text = await File.ReadAllTextAsync(file, cancellationToken);
            pointedTo.UnionWith(CommonLicence().Matches(text).Select(match => match.Groups[1].Value));
        }

        IEnumerable<string> held = Directory.EnumerateFiles(Path.Combine(folder, "common-licenses")).Select(file => Path.GetFileName(file));

        Assert.Contains("GPL-2", pointedTo);
        Assert.Equal(pointedTo, held.Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task TheLockListsTheSourceOfEveryPackageAndOfWhatItWasBuiltWith()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        using JsonDocument lockFile = await ReadLockAsync(Repository.Root(), cancellationToken);
        HashSet<(string?, string?)> listed = [.. Sources(lockFile).Select(source => (Text(source, "name"), Text(source, "version")))];

        List<(string?, string?)> needed = [];
        foreach (JsonElement package in Packages(lockFile))
        {
            needed.Add((Text(package, "source"), Text(package, "sourceVersion")));
            needed.AddRange(package.GetProperty("builtUsing").EnumerateArray()
                .Select(used => (Text(used, "source"), Text(used, "version"))));
        }

        Assert.Contains(("glibc", Text(Packages(lockFile).Single(package => Name(package) == "libc6"), "version")), needed);
        Assert.Empty(needed.Except(listed));
        Assert.Empty(listed.Except(needed));
    }

    [Fact]
    public async Task EverySourceInTheLockHasItsFilesWithTheirHashes()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        using JsonDocument lockFile = await ReadLockAsync(Repository.Root(), cancellationToken);

        foreach (JsonElement source in Sources(lockFile))
        {
            List<JsonElement> files = [.. source.GetProperty("files").EnumerateArray()];
            bool leavesOut = source.TryGetProperty("leftOut", out JsonElement leftOut);

            Assert.Contains(files, file => Name(file).EndsWith(".dsc", StringComparison.Ordinal));
            Assert.All(leavesOut ? [.. files, .. leftOut.EnumerateArray()] : files, file =>
            {
                Assert.Matches("^[0-9a-f]{64}$", Text(file, "sha256"));
                Assert.True(file.GetProperty("size").GetInt64() > 0);
            });
            bool binaryOnly = Text(source, "directory")?.StartsWith(BinaryOnlyPool, StringComparison.Ordinal) == true;
            Assert.True(!leavesOut || binaryOnly, Name(source));
        }
    }

    // sources.sh writes SOURCES.md from the lock, and Update-LinuxBootLock.ps1 puts it into the licences folder.
    [Fact]
    public async Task TheListOfSourcesInTheLicencesFolderIsTheLocks()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        string root = Repository.Root();
        string list = (await File.ReadAllTextAsync(Path.Combine(root, "licenses", "linux-boot", "SOURCES.md"), cancellationToken))
            .ReplaceLineEndings("\n");
        using JsonDocument lockFile = await ReadLockAsync(root, cancellationToken);

        Assert.Contains($"snapshot {Text(lockFile.RootElement, "snapshot")}.", list, StringComparison.Ordinal);

        foreach (JsonElement source in Sources(lockFile))
        {
            Assert.Contains($"## {Name(source)} {Text(source, "version")}\n", list, StringComparison.Ordinal);
            Assert.All(source.GetProperty("files").EnumerateArray(), file => Assert.Contains(
                $"| `{Name(file)}` | {file.GetProperty("size").GetInt64()} | `{Text(file, "sha256")}` |",
                list,
                StringComparison.Ordinal));
        }
    }

    private static string CopyrightFile(string source) => $"licenses/linux-boot/{source}.copyright";

    private static IEnumerable<JsonElement> Packages(JsonDocument lockFile) =>
        lockFile.RootElement.GetProperty("packages").EnumerateArray();

    private static IEnumerable<JsonElement> Sources(JsonDocument lockFile) =>
        lockFile.RootElement.GetProperty("sources").EnumerateArray();

    private static string Name(JsonElement element) => Text(element, "name") ?? string.Empty;

    private static string? Text(JsonElement element, string property) => element.GetProperty(property).GetString();

    private static async Task<JsonDocument> ReadLockAsync(string root, CancellationToken cancellationToken)
    {
        await using FileStream file = File.OpenRead(Path.Combine(root, "build", "LinuxBoot", "packages.lock.json"));

        return await JsonDocument.ParseAsync(file, cancellationToken: cancellationToken);
    }

    [GeneratedRegex("/usr/share/common-licenses/([A-Za-z0-9.+-]*[A-Za-z0-9])")]
    private static partial Regex CommonLicence();
}
