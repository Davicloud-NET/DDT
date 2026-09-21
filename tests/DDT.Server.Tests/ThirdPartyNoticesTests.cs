// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Text.Json;
using Xunit;

namespace DDT.Server.Tests;

// Every package whose code ends up in the server, the agent or the web bundle has to be named in
// THIRD-PARTY-NOTICES.md, in backticks, so that a new dependency cannot ship without its notice.
public sealed class ThirdPartyNoticesTests
{
    private static readonly string[] s_outputAssetKinds = ["runtime", "native", "runtimeTargets"];

    [Fact]
    public async Task NamesEveryShippedPackage()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        string root = RepositoryRoot();
        string notices = await File.ReadAllTextAsync(Path.Combine(root, "THIRD-PARTY-NOTICES.md"), cancellationToken);

        List<string> shipped = [];
        shipped.AddRange(await NuGetPackagesAsync(Path.Combine(root, "src", "DDT.Host", "obj", "project.assets.json"), cancellationToken));
        shipped.AddRange(await NuGetPackagesAsync(Path.Combine(root, "src", "DDT.Agent", "obj", "project.assets.json"), cancellationToken));
        shipped.AddRange(await NpmPackagesAsync(Path.Combine(root, "src", "DDT.Web", "package-lock.json"), cancellationToken));

        Assert.Contains("ManagedWimLib", shipped);
        Assert.Contains("react", shipped);

        IEnumerable<string> unnamed = shipped.Distinct().Where(name => !notices.Contains($"`{name}`", StringComparison.Ordinal));
        Assert.Empty(unnamed.Order(StringComparer.Ordinal));
    }

    // A package ships when it puts a runtime or native file into the output. Compile-time packages have none, and
    // packages whose assets are excluded, as the agent does with ManagedWimLib's managed code, list only _._.
    private static async Task<List<string>> NuGetPackagesAsync(string path, CancellationToken cancellationToken)
    {
        using JsonDocument assets = await ReadJsonAsync(path, cancellationToken);

        return assets.RootElement.GetProperty("targets").EnumerateObject()
            .SelectMany(target => target.Value.EnumerateObject())
            .Where(library => library.Value.GetProperty("type").GetString() == "package" && HasOutputFiles(library.Value))
            .Select(library => library.Name.Split('/')[0])
            .ToList();
    }

    private static bool HasOutputFiles(JsonElement library) =>
        s_outputAssetKinds.Any(kind =>
            library.TryGetProperty(kind, out JsonElement files)
            && files.EnumerateObject().Any(file => Path.GetFileName(file.Name) != "_._"));

    private static async Task<List<string>> NpmPackagesAsync(string path, CancellationToken cancellationToken)
    {
        using JsonDocument lockFile = await ReadJsonAsync(path, cancellationToken);

        return lockFile.RootElement.GetProperty("packages").EnumerateObject()
            .Where(package => package.Name.Length > 0 && !(package.Value.TryGetProperty("dev", out JsonElement dev) && dev.GetBoolean()))
            .Select(package => package.Name.Split("node_modules/")[^1])
            .ToList();
    }

    private static async Task<JsonDocument> ReadJsonAsync(string path, CancellationToken cancellationToken)
    {
        await using FileStream file = File.OpenRead(path);

        return await JsonDocument.ParseAsync(file, cancellationToken: cancellationToken);
    }

    private static string RepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "DDT.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException($"No DDT.slnx above {AppContext.BaseDirectory}.");
    }
}
