// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Text.Json;
using System.Text.RegularExpressions;
using DDT.Server.LinuxBoot;
using DDT.Server.Machines;
using Xunit;

namespace DDT.Server.Tests;

// A release ships the Linux boot environment in the MSI and in the server's image, and it holds programs under the
// GPL. So wherever a release puts it, the same release has to put its source code. These tests read the workflows and
// the build files, and fail when a change would ship the environment without its sources, or sign or cache it.
public sealed partial class LinuxBootReleaseTests
{
    private const string SourcesArchive = "ddt-linux-boot-sources.tar";
    private const string SourcesImage = "ghcr.io/davicloud-net/ddt-sources";

    [Fact]
    public async Task AReleaseShipsTheBootEnvironmentInTheMsiAndInTheImage()
    {
        Dictionary<string, string> jobs = Jobs(await ReadAsync(".github", "workflows", "release.yml"));
        string dockerfile = await ReadAsync("build", "Dockerfile");
        string installer = await ReadAsync("build", "Build-Installer.ps1");

        // The names are the ones the server looks for next to its program files.
        Assert.Contains($"artifacts/installer-work/payload/{LinuxBootEnvironment.FolderName}/", jobs["installer"], StringComparison.Ordinal);
        Assert.Contains($"artifacts/installer-work/payload/{AgentReleaseStores.LinuxFileName}", jobs["installer"], StringComparison.Ordinal);
        Assert.Contains($"Join-Path $payload '{LinuxBootEnvironment.FolderName}'", installer, StringComparison.Ordinal);
        Assert.Contains($"Join-Path $payload '{AgentReleaseStores.LinuxFileName}'", installer, StringComparison.Ordinal);
        Assert.Contains($"/context/artifacts/bundle/{LinuxBootEnvironment.FolderName} /app/{LinuxBootEnvironment.FolderName}", dockerfile, StringComparison.Ordinal);
        Assert.Contains($"ddt-console.zip {AgentReleaseStores.LinuxFileName};", dockerfile, StringComparison.Ordinal);

        // Neither job would say so if the files were not there, so each checks that they are.
        Assert.Contains("The MSI's payload has no $file.", jobs["installer"], StringComparison.Ordinal);
        Assert.Contains("The bundle has no $file.", jobs["container"], StringComparison.Ordinal);
        Assert.Contains("needs: [programs, sign-programs, linux]", jobs["installer"], StringComparison.Ordinal);
    }

    [Fact]
    public async Task AReleasePublishesTheSourcesBesideTheMsi()
    {
        Dictionary<string, string> jobs = Jobs(await ReadAsync(".github", "workflows", "release.yml"));

        Assert.Contains("sh build/LinuxBoot/sources.sh build/LinuxBoot/packages.lock.json", jobs["linux"], StringComparison.Ordinal);
        Assert.Contains($"tar --create --file artifacts/linux-boot-sources/{SourcesArchive}", jobs["linux"], StringComparison.Ordinal);

        // Among the release's files before their sums are written, so SHA256SUMS lists it.
        string copied = $@"Copy-Item artifacts\linux-boot-sources\{SourcesArchive} $release";
        Assert.Contains(copied, jobs["files"], StringComparison.Ordinal);
        Assert.True(
            jobs["files"].IndexOf(copied, StringComparison.Ordinal) < jobs["files"].IndexOf("'SHA256SUMS'", StringComparison.Ordinal),
            "The sources have to be among the release's files before SHA256SUMS is written.");

        string create = Regex.Replace(jobs["release"], @"\\\r?\n\s*", string.Empty);
        Assert.Matches($@"gh release create ""v\$VERSION"" [^\n]*release/DDT\.msi [^\n]*release/{Regex.Escape(SourcesArchive)} [^\n]*release/SHA256SUMS", create);
    }

    [Fact]
    public async Task AReleasePushesTheSourcesBesideTheServersImageAndBeforeIt()
    {
        Dictionary<string, string> jobs = Jobs(await ReadAsync(".github", "workflows", "release.yml"));
        string container = jobs["container"];
        string sources = await ReadAsync("build", "Dockerfile.sources");

        int sourcesPushed = container.IndexOf("- name: Build and push the sources", StringComparison.Ordinal);
        int imagePushed = container.IndexOf("- name: Build and push\n", StringComparison.Ordinal);

        Assert.True(sourcesPushed >= 0 && sourcesPushed < imagePushed, "The sources are pushed first, so the image is never there without them.");
        Assert.Contains("file: build/Dockerfile.sources", container, StringComparison.Ordinal);
        Assert.Contains($"sources=\"{SourcesImage}:$VERSION\"", container, StringComparison.Ordinal);
        Assert.Contains("tags: ${{ steps.tags.outputs.sources }}", container, StringComparison.Ordinal);

        // Whatever pushes the image pushes the sources: both steps decide by the same words.
        Assert.Equal(2, Regex.Count(container, Regex.Escape("push: ${{ !inputs.dry_run }}")));
        Assert.Contains($"COPY {SourcesArchive} /", sources, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheNoticesAndTheReleasesTextSayWhereTheSourcesAre()
    {
        string notices = await ReadAsync("THIRD-PARTY-NOTICES.md");
        string releaseText = await ReadAsync("build", "release-notes.md");
        string list = await ReadAsync("licenses", "linux-boot", "SOURCES.md");

        Assert.All((string[])[notices, releaseText], text =>
        {
            Assert.Contains($"`{SourcesArchive}`", text, StringComparison.Ordinal);
            Assert.Contains(SourcesImage, text, StringComparison.Ordinal);
        });
        Assert.Contains($"`{SourcesArchive}`", list, StringComparison.Ordinal);
        Assert.Contains($"`{AgentReleaseStores.LinuxFileName}`", notices, StringComparison.Ordinal);
    }

    // The signing jobs hold the maintainer's certificate and stay as bare as they are. The Linux files need neither.
    [Fact]
    public async Task TheLinuxFilesAreBuiltWithoutSecretsOrACacheAndAreNotSigned()
    {
        Dictionary<string, string> jobs = Jobs(await ReadAsync(".github", "workflows", "release.yml"));
        string linux = jobs["linux"];

        Assert.DoesNotContain("secrets.", linux, StringComparison.Ordinal);
        Assert.DoesNotContain("environment:", linux, StringComparison.Ordinal);
        Assert.DoesNotContain("permissions:", linux, StringComparison.Ordinal);
        Assert.DoesNotContain("cache", linux.Replace("--no-cache", string.Empty, StringComparison.Ordinal), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(2, Regex.Count(linux, "docker buildx build "));
        Assert.Equal(2, Regex.Count(linux, "--no-cache"));
        Assert.All((string[])["sign-programs", "sign-installer"], job => Assert.DoesNotContain("linux", jobs[job], StringComparison.OrdinalIgnoreCase));
        Assert.Contains("DDT_VERSION=$VERSION", linux, StringComparison.Ordinal);
        Assert.Contains("'.agent.version == $version'", linux, StringComparison.Ordinal);
    }

    // One lock has to give one base.img wherever it is built. The lock names it, and both workflows compare.
    [Fact]
    public async Task TheLockNamesTheBaseImageThatEveryBuildIsComparedWith()
    {
        using JsonDocument lockFile = JsonDocument.Parse(await ReadAsync("build", "LinuxBoot", "packages.lock.json"));
        string update = await ReadAsync("build", "Update-LinuxBootLock.ps1");
        Dictionary<string, string> release = Jobs(await ReadAsync(".github", "workflows", "release.yml"));
        Dictionary<string, string> checks = Jobs(await ReadAsync(".github", "workflows", "ci.yml"));

        Assert.Matches("^[0-9a-f]{64}$", lockFile.RootElement.GetProperty("expected").GetProperty("baseImageSha256").GetString());
        Assert.Contains("Set-ExpectedBaseImage -LockPath $lockPath -Sha256 $hash", update, StringComparison.Ordinal);
        Assert.All((string[])[release["linux"], checks["linux-boot"]], job =>
        {
            Assert.Contains(".expected.baseImageSha256 build/LinuxBoot/packages.lock.json", job, StringComparison.Ordinal);
            Assert.Contains("artifacts/linux-boot/base.img\" | sha256sum --check", job, StringComparison.Ordinal);
        });
    }

    // A start script nobody listed would go unchecked. Every shell script of the boot environment has to be named in
    // the job, by itself or by its folder.
    [Fact]
    public async Task ContinuousIntegrationChecksEveryShellScriptOfTheBootEnvironment()
    {
        string root = Repository.Root();
        string job = Jobs(await ReadAsync(".github", "workflows", "ci.yml"))["linux-boot"];
        string folder = Path.Combine(root, "build", "LinuxBoot");

        Assert.Contains("shellcheck --shell=sh -x -P build/LinuxBoot/rootfs", job, StringComparison.Ordinal);

        List<string> scripts = [];

        foreach (string file in Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories))
        {
            using StreamReader reader = new(file);
            char[] start = new char[9];
            int read = await reader.ReadBlockAsync(start, TestContext.Current.CancellationToken);

            if (new string(start, 0, read) == "#!/bin/sh")
            {
                scripts.Add(Path.GetRelativePath(root, file).Replace('\\', '/'));
            }
        }

        Assert.Contains("build/LinuxBoot/rootfs/init", scripts);
        Assert.Contains("build/LinuxBoot/rootfs/usr/lib/ddt/session", scripts);
        IEnumerable<string> missed = scripts.Where(script =>
            !job.Contains($" {script}", StringComparison.Ordinal)
            && !job.Contains($" {script[..(script.LastIndexOf('/') + 1)]}*", StringComparison.Ordinal));
        Assert.Empty(missed.Order(StringComparer.Ordinal));
    }

    // A tag of an action moves, and whoever moves it runs in these jobs. A commit does not move.
    [Theory]
    [InlineData("ci.yml")]
    [InlineData("release.yml")]
    public async Task EveryActionIsPinnedByItsCommit(string workflow)
    {
        string text = await ReadAsync(".github", "workflows", workflow);

        MatchCollection used = Uses().Matches(text);

        Assert.NotEmpty(used);
        Assert.All(used, use => Assert.Matches("^[A-Za-z0-9_.-]+/[A-Za-z0-9_./-]+@[0-9a-f]{40} # v[0-9]+\\.[0-9]+\\.[0-9]+$", use.Groups["action"].Value.Trim()));
    }

    private static async Task<string> ReadAsync(params string[] path) =>
        (await File.ReadAllTextAsync(Path.Combine([Repository.Root(), .. path]), TestContext.Current.CancellationToken)).ReplaceLineEndings("\n");

    // The text of each job of a workflow, by the name it has under jobs.
    private static Dictionary<string, string> Jobs(string workflow)
    {
        string jobs = workflow[workflow.IndexOf("\njobs:\n", StringComparison.Ordinal)..];
        MatchCollection names = JobName().Matches(jobs);
        Dictionary<string, string> found = new(StringComparer.Ordinal);

        for (int index = 0; index < names.Count; index++)
        {
            int end = index + 1 < names.Count ? names[index + 1].Index : jobs.Length;
            found[names[index].Groups["name"].Value] = jobs[names[index].Index..end];
        }

        return found;
    }

    [GeneratedRegex(@"^  (?<name>[a-z][a-z0-9-]*):$", RegexOptions.Multiline)]
    private static partial Regex JobName();

    [GeneratedRegex(@"^\s*(?:- )?uses: (?<action>.+)$", RegexOptions.Multiline)]
    private static partial Regex Uses();
}
