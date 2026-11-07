// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Agent.Deployment;
using DDT.Agent.Sequences;
using DDT.Contracts.Agents;
using Xunit;

namespace DDT.Agent.Tests;

// The small parts of the agent for Linux: free space, a tool's odd output, a script's environment, the legal texts.
public sealed class LinuxPartsTests : IDisposable
{
    private readonly LinuxTree _tree = new();

    public void Dispose() => _tree.Dispose();

    // A package is unpacked into a folder that is made only afterwards.
    [Fact]
    public void CountsTheFreeSpaceOfAPathThatDoesNotExistYet()
    {
        LinuxFreeSpace space = new();
        long here = space.AvailableBytes(_tree.Root);

        Assert.True(here > 0);
        Assert.InRange(space.AvailableBytes(_tree.PathOf("not", "made", "yet")), here / 2, here * 2);
    }

    // A tool of the image's own, such as one that prints names in Latin-1, must not lose a line to its encoding.
    [Fact]
    public void ReadsALineThatIsNotUtf8ByteForByte()
    {
        byte[] latin1 = [(byte)'G', 0xFC, (byte)'n', (byte)'t', (byte)'e', (byte)'r', 0xFF];

        Assert.Equal("Günterÿ", Latin1Output.Decode(latin1));
        Assert.Equal("Günterÿ", ToolOutput.Decode(latin1, Latin1Output.Decode));
        Assert.Equal("Günter", ToolOutput.Decode("Günter"u8, Latin1Output.Decode));
    }

    // busybox init starts the session with a PATH, a service manager may start the agent with nothing.
    [Fact]
    public void GivesAScriptAPathAndAHomeWhateverStartedTheAgent()
    {
        Dictionary<string, string?> own = new() { ["PATH"] = "/usr/bin", ["HOME"] = "/root/agent" };
        Dictionary<string, string?> bare = new() { ["PATH"] = "" };

        Assert.Equal(
            new Dictionary<string, string> { ["PATH"] = "/usr/bin", ["HOME"] = "/root/agent" },
            LinuxShell.Environment(name => own.GetValueOrDefault(name)));
        Assert.Equal(
            new Dictionary<string, string> { ["PATH"] = LinuxShell.DefaultPath, ["HOME"] = "/root" },
            LinuxShell.Environment(name => bare.GetValueOrDefault(name)));
        Assert.Contains("/usr/bin", LinuxShell.DefaultPath.Split(':'));
    }

    [Fact]
    public void TheAgentForLinuxNamesItsBootEnvironmentAndKeepsItsRunInMemory()
    {
        Assert.Equal("/run/ddt", LinuxBoot.RunDirectory);
        Assert.StartsWith(LinuxBoot.RunDirectory, LinuxBoot.WorkDirectory, StringComparison.Ordinal);
        Assert.Equal("ddt-agent", LinuxBoot.AgentFileName);
        Assert.DoesNotContain("Windows", EnvironmentTexts.Usage(AgentEnvironment.LinuxBoot), StringComparison.Ordinal);
    }

    [Fact]
    public void PrintsTheNoticesTheGplAsksForAtEveryStart()
    {
        StringWriter output = new();

        LinuxLegalNotices.WriteStartupNotices(output);

        Assert.Equal(
            [
                "DDT, the Davicloud Deployment Toolkit. Copyright (C) 2026 Davicloud.",
                "This program comes with ABSOLUTELY NO WARRANTY.",
                "It is free software: you can redistribute it and modify it under the GNU General Public License, version 3 or later, with additional terms.",
                "Run ddt-agent --licenses to read the licences.",
                "",
                "",
            ],
            output.ToString().Split(Environment.NewLine));
    }

    // The file reaches machines on its own, so every text it lists is inside it.
    [Fact]
    public void CarriesEveryLicenceItLists()
    {
        StringWriter output = new();

        LinuxLegalNotices.WriteLicenses(output);

        string licenses = output.ToString();
        Assert.All(LinuxLegalNotices.Files, file => Assert.Contains($"======== {file} ========", licenses, StringComparison.Ordinal));
        Assert.All(LinuxLegalNotices.Files, file => Assert.NotEmpty(LinuxLegalNotices.ReadText(file)));
        Assert.Equal(LegalTexts.AttributionNotice, LinuxLegalNotices.ReadText("NOTICE").Split('\n')[0].TrimEnd('\r'));
        Assert.Contains("GNU GENERAL PUBLIC LICENSE", LinuxLegalNotices.ReadText("LICENSE"), StringComparison.Ordinal);
        Assert.DoesNotContain("wimlib", string.Join(' ', LinuxLegalNotices.Files), StringComparison.OrdinalIgnoreCase);
    }

    // OpenSSL, musl and the compiler's support code are linked into the published file, which no package list shows.
    [Fact]
    public void ListsEveryLicenceTextThatConcernsTheFileAndPrintsItAsInTheRepository()
    {
        string root = RepositoryRoot();
        string[] folders = ["dotnet", "gcc", "musl", "openssl", "zstd"];
        string[] expected =
        [
            "LICENSE",
            "NOTICE",
            "THIRD-PARTY-NOTICES.md",
            .. folders
                .SelectMany(folder => Directory.GetFiles(Path.Combine(root, "licenses", folder)))
                .Select(path => Path.GetRelativePath(root, path).Replace('\\', '/')),
        ];

        Assert.Equal(expected.Order(StringComparer.Ordinal), LinuxLegalNotices.Files.Order(StringComparer.Ordinal));
        Assert.All(LinuxLegalNotices.Files, file => Assert.Equal(
            File.ReadAllText(Path.Combine(root, file)).ReplaceLineEndings("\n"),
            LinuxLegalNotices.ReadText(file).ReplaceLineEndings("\n")));
        Assert.Contains("Apache License", LinuxLegalNotices.ReadText("licenses/openssl/LICENSE.txt"), StringComparison.Ordinal);
        Assert.Contains("Rich Felker", LinuxLegalNotices.ReadText("licenses/musl/COPYRIGHT"), StringComparison.Ordinal);
        Assert.Contains("GCC RUNTIME LIBRARY EXCEPTION", LinuxLegalNotices.ReadText("licenses/gcc/COPYING.RUNTIME"), StringComparison.Ordinal);
    }

    // The build writes the version into the file as text, where the server finds it without running the file.
    [Fact]
    public void CarriesItsVersionAfterAMarkerAProgramCanSearchFor()
    {
        System.Reflection.Assembly agent = typeof(LinuxLegalNotices).Assembly;
        using Stream marker = agent.GetManifestResourceStream("ddt-agent-version") ?? throw new InvalidOperationException("The agent carries no version marker.");
        using StreamReader reader = new(marker);

        Assert.Equal($"DDT-AGENT-VERSION:{agent.GetName().Version?.ToString(3)}", reader.ReadToEnd().TrimEnd('\r', '\n'));
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
