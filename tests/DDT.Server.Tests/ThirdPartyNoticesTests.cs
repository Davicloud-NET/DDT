// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Text.Json;
using Xunit;

namespace DDT.Server.Tests;

// Every package whose code ends up in the server, the agent, the console or the web bundle has to be named in
// THIRD-PARTY-NOTICES.md, in backticks, and every package of the web bundle needs its licence text in
// licenses/web/THIRD-PARTY-LICENSES.txt at the version the lock file holds, so that a new or upgraded
// dependency cannot ship without its notice.
public sealed class ThirdPartyNoticesTests
{
    private static readonly string[] s_outputAssetKinds = ["runtime", "native", "runtimeTargets"];

    // What the console is published for: NativeAOT, Windows PE on x64.
    private const string ConsoleTarget = "net10.0-windows/win-x64";

    // Development packages whose own code the bundle contains: Rolldown's runtime helpers and the module preload
    // polyfill Vite asks it for.
    private static readonly string[] s_bundledBuildTools = ["rolldown", "vite", "tailwindcss"];

    // Each entry in the web licence file starts with a line of this, followed by "<name> <version>".
    private static readonly string s_webEntrySeparator = new('=', 100);

    [Fact]
    public async Task NamesEveryShippedPackage()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        string root = Repository.Root();
        string notices = await File.ReadAllTextAsync(Path.Combine(root, "THIRD-PARTY-NOTICES.md"), cancellationToken);

        List<string> shipped = [];
        shipped.AddRange(await NuGetPackagesAsync(Path.Combine(root, "src", "DDT.Host", "obj", "project.assets.json"), cancellationToken));
        shipped.AddRange(await NuGetPackagesAsync(Path.Combine(root, "src", "DDT.Agent", "obj", "project.assets.json"), cancellationToken));
        shipped.AddRange(await NuGetPackagesAsync(
            Path.Combine(root, "src", "DDT.MachineConsole", "obj", "project.assets.json"),
            cancellationToken,
            ConsoleTarget));
        shipped.AddRange((await WebBundlePackagesAsync(root, cancellationToken)).Select(package => package.Name));

        Assert.Contains("ManagedWimLib", shipped);
        Assert.Contains("SkiaSharp.NativeAssets.Win32", shipped);
        Assert.Contains("react", shipped);
        Assert.Contains("vite", shipped);

        IEnumerable<string> unnamed = shipped.Distinct().Where(name => !notices.Contains($"`{name}`", StringComparison.Ordinal));
        Assert.Empty(unnamed.Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task CarriesTheLicenceOfEveryWebPackageAtItsLockedVersion()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        string root = Repository.Root();
        string[] lines = await File.ReadAllLinesAsync(Path.Combine(root, "licenses", "web", "THIRD-PARTY-LICENSES.txt"), cancellationToken);

        IEnumerable<string> expected = (await WebBundlePackagesAsync(root, cancellationToken))
            .Select(package => $"{package.Name} {package.Version}");
        IEnumerable<string> entries = lines.Zip(lines.Skip(1)).Where(pair => pair.First == s_webEntrySeparator).Select(pair => pair.Second);

        Assert.Equal(expected.Order(StringComparer.Ordinal), entries.Order(StringComparer.Ordinal));
    }

    // A package ships when it puts a runtime or native file into the output. Compile-time packages have none, and
    // packages whose assets are excluded, as the agent does with ManagedWimLib's managed code, list only _._. Where
    // target names one, only that target counts, such as the console's win-x64, which leaves out the native assets for
    // Linux and macOS that the target without a runtime lists.
    private static async Task<List<string>> NuGetPackagesAsync(string path, CancellationToken cancellationToken, string? target = null)
    {
        using JsonDocument assets = await ReadJsonAsync(path, cancellationToken);

        return assets.RootElement.GetProperty("targets").EnumerateObject()
            .Where(candidate => target is null || candidate.Name == target)
            .SelectMany(candidate => candidate.Value.EnumerateObject())
            .Where(library => library.Value.GetProperty("type").GetString() == "package" && HasOutputFiles(library.Value))
            .Select(library => library.Name.Split('/')[0])
            .ToList();
    }

    private static bool HasOutputFiles(JsonElement library) =>
        s_outputAssetKinds.Any(kind =>
            library.TryGetProperty(kind, out JsonElement files)
            && files.EnumerateObject().Any(file => Path.GetFileName(file.Name) != "_._"));

    // The non-development closure of the web UI's lock file, and the build tools whose code the bundle contains.
    private static async Task<List<(string Name, string? Version)>> WebBundlePackagesAsync(string root, CancellationToken cancellationToken)
    {
        using JsonDocument lockFile = await ReadJsonAsync(Path.Combine(root, "src", "DDT.Web", "package-lock.json"), cancellationToken);

        return lockFile.RootElement.GetProperty("packages").EnumerateObject()
            .Where(package => package.Name.Length > 0)
            .Select(package => (
                Name: package.Name.Split("node_modules/")[^1],
                Version: package.Value.GetProperty("version").GetString(),
                Dev: package.Value.TryGetProperty("dev", out JsonElement dev) && dev.GetBoolean()))
            .Where(package => !package.Dev || s_bundledBuildTools.Contains(package.Name))
            .Select(package => (package.Name, package.Version))
            .ToList();
    }

    private static async Task<JsonDocument> ReadJsonAsync(string path, CancellationToken cancellationToken)
    {
        await using FileStream file = File.OpenRead(path);

        return await JsonDocument.ParseAsync(file, cancellationToken: cancellationToken);
    }
}
