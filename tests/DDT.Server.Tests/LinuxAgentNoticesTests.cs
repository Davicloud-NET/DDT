// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Text.RegularExpressions;
using Xunit;

namespace DDT.Server.Tests;

// The agent for Linux is linked in a container, where the linker adds libraries no package restore lists. What
// build/Dockerfile.agent-linux installs to link statically must be named in THIRD-PARTY-NOTICES.md, and the build
// must be handed every licence text the agent embeds, or the published file would print none of it.
public sealed partial class LinuxAgentNoticesTests
{
    private const string Dockerfile = "build/Dockerfile.agent-linux";

    // What the linker takes whole from the system it runs on: the C library, and any package of static libraries.
    private static readonly string[] s_alwaysLinked = ["musl-dev"];

    [Fact]
    public async Task TheNoticesNameWhatTheBuildLinksIntoTheFile()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        string root = Repository.Root();
        string notices = await File.ReadAllTextAsync(Path.Combine(root, "THIRD-PARTY-NOTICES.md"), cancellationToken);
        string dockerfile = await File.ReadAllTextAsync(Path.Combine(root, Dockerfile), cancellationToken);

        List<string> installed =
        [
            .. Installed().Matches(dockerfile)
                .SelectMany(match => match.Groups["packages"].Value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                .Select(package => package.Trim('"').Split('=')[0]),
        ];
        List<string> linked = [.. installed.Where(package => package.EndsWith("-static", StringComparison.Ordinal)), .. s_alwaysLinked];

        Assert.Contains("openssl-libs-static", linked);
        Assert.DoesNotContain(linked, package => !notices.Contains($"`{package}`", StringComparison.Ordinal));
        Assert.Contains("## The agent for Linux, `ddt-agent`", notices, StringComparison.Ordinal);
        Assert.Contains("`build/Publish-LinuxAgent.ps1`", notices, StringComparison.Ordinal);
    }

    // Alpine has one build of a package at a time, and the agent would get whichever is there that day. The build
    // names the two it links in by their versions, and the notices name the same ones.
    [Fact]
    public async Task TheBuildPinsWhatItLinksInAtTheVersionsTheNoticesName()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        string root = Repository.Root();
        string notices = Whitespace().Replace(await File.ReadAllTextAsync(Path.Combine(root, "THIRD-PARTY-NOTICES.md"), cancellationToken), " ");
        string dockerfile = await File.ReadAllTextAsync(Path.Combine(root, Dockerfile), cancellationToken);
        Match openssl = Pinned("OPENSSL").Match(dockerfile);
        Match musl = Pinned("MUSL").Match(dockerfile);

        Assert.True(openssl.Success && musl.Success, "The Dockerfile names no version of OpenSSL or of musl.");
        Assert.Contains("\"openssl-dev=${OPENSSL}\" \"openssl-libs-static=${OPENSSL}\" \"musl-dev=${MUSL}\"", dockerfile, StringComparison.Ordinal);
        Assert.Contains($"OpenSSL {openssl.Groups["version"].Value}, whose libcrypto and libssl", notices, StringComparison.Ordinal);
        Assert.Contains($"`openssl-libs-static` package {openssl.Groups["version"].Value}-r{openssl.Groups["build"].Value}", notices, StringComparison.Ordinal);
        Assert.Contains($"musl {musl.Groups["version"].Value}, Alpine's C library, from its `musl-dev` package {musl.Groups["version"].Value}-r{musl.Groups["build"].Value}", notices, StringComparison.Ordinal);
    }

    // The project embeds whole folders of licences. A folder the build is not handed embeds as nothing, without an error.
    [Fact]
    public async Task TheBuildIsHandedEveryLicenceFolderTheAgentEmbeds()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        string root = Repository.Root();
        string project = await File.ReadAllTextAsync(Path.Combine(root, "src", "DDT.Agent.Linux", "DDT.Agent.Linux.csproj"), cancellationToken);
        string dockerfile = await File.ReadAllTextAsync(Path.Combine(root, Dockerfile), cancellationToken);
        string[] ignored = await File.ReadAllLinesAsync(Path.Combine(root, Dockerfile + ".dockerignore"), cancellationToken);

        List<string> folders = [.. EmbeddedFolder().Matches(project).Select(match => match.Groups["folder"].Value).Order(StringComparer.Ordinal)];

        Assert.Equal(["dotnet", "gcc", "musl", "openssl", "zstd"], folders);
        Assert.All(folders, folder => Assert.Contains($"COPY licenses/{folder}/ licenses/{folder}/", dockerfile, StringComparison.Ordinal));
        Assert.All(folders, folder => Assert.Contains($"!licenses/{folder}/", ignored));
        Assert.All(folders, folder => Assert.NotEmpty(Directory.GetFiles(Path.Combine(root, "licenses", folder))));
        Assert.Contains("COPY LICENSE NOTICE THIRD-PARTY-NOTICES.md ./", dockerfile, StringComparison.Ordinal);
    }

    // A tag moves. The digest keeps the compiler and the C library the same, and with them the notices true.
    [Fact]
    public async Task TheBuildsImageIsPinnedByItsDigest()
    {
        string dockerfile = await File.ReadAllTextAsync(Path.Combine(Repository.Root(), Dockerfile), TestContext.Current.CancellationToken);

        Assert.Matches(@"(?m)^ARG DOTNET_SDK=mcr\.microsoft\.com/dotnet/sdk:10\.0-alpine@sha256:[0-9a-f]{64}$", dockerfile);
    }

    [GeneratedRegex(@"apk add --no-cache (?<packages>[a-zA-Z0-9+ ._${}=""-]+)")]
    private static partial Regex Installed();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    private static Regex Pinned(string name) => new($@"(?m)^ARG {name}=(?<version>\d+\.\d+\.\d+)-r(?<build>\d+)$", RegexOptions.None, TimeSpan.FromSeconds(1));

    [GeneratedRegex(@"<EmbeddedResource Include=""\.\.\\\.\.\\licenses\\(?<folder>[a-z0-9-]+)\\\*""")]
    private static partial Regex EmbeddedFolder();
}
