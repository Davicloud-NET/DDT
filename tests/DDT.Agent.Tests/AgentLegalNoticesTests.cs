// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using DDT.Agent.Deployment;
using Xunit;

namespace DDT.Agent.Tests;

public sealed class AgentLegalNoticesTests
{
    private static readonly string[] s_startupLines =
    [
        "DDT, the Davicloud Deployment Toolkit. Copyright (C) 2026 Davicloud.",
        "This program comes with ABSOLUTELY NO WARRANTY.",
        "It is free software: you can redistribute it and modify it under the GNU General Public License, version 3 or later, " +
        "with additional terms.",
        "It uses wimlib, Copyright 2012-2023 Eric Biggers, which is under the GNU Lesser General Public License, version 3 or later.",
        "Run ddt-agent --licenses to read the licences.",
    ];

    [Fact]
    public void WritesTheLegalNoticesForStartUp()
    {
        StringWriter output = new();

        AgentLegalNotices.WriteStartupNotices(output);

        Assert.Equal(string.Concat(s_startupLines.Select(line => line + output.NewLine)) + output.NewLine, output.ToString());
    }

    [Fact]
    public void TakesTheNoticesFromTheTextsItCarries()
    {
        string notices = AgentLegalNotices.ReadText("THIRD-PARTY-NOTICES.md");

        Assert.Equal(AgentLegalNotices.AttributionNotice, AgentLegalNotices.ReadText("NOTICE").Split('\n')[0]);
        Assert.Contains($"- {AgentLegalNotices.WimlibCopyright}, https://wimlib.net.", notices, StringComparison.Ordinal);
    }

    // The notices name the source of the agent's libwim by the DLL's hash. A new ManagedWimLib brings a new DLL, and
    // with it a new source to name.
    [Fact]
    public void NamesTheSourceOfTheLibwimItCarries()
    {
        using Stream library = typeof(WimLibraryFile).Assembly.GetManifestResourceStream(WimLibraryFile.FileName)
            ?? throw new InvalidOperationException($"The agent carries no {WimLibraryFile.FileName}.");
        string sha256 = Convert.ToHexStringLower(SHA256.HashData(library));

        Assert.Contains($"`{sha256}`", AgentLegalNotices.ReadText("THIRD-PARTY-NOTICES.md"), StringComparison.Ordinal);
    }

    [Fact]
    public void CarriesEveryLicenceTextThatConcernsTheAgent()
    {
        string root = RepositoryRoot();
        string[] folders = ["dotnet", "wimlib", "zstd"];
        string[] expected =
        [
            "LICENSE",
            "NOTICE",
            "THIRD-PARTY-NOTICES.md",
            .. folders
                .SelectMany(folder => Directory.GetFiles(Path.Combine(root, "licenses", folder)))
                .Select(path => Path.GetRelativePath(root, path).Replace('\\', '/')),
        ];

        Assert.Equal(expected.Order(StringComparer.Ordinal), AgentLegalNotices.Files.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void WritesEveryTextAsInTheRepositoryUnderItsOwnHeading()
    {
        string root = RepositoryRoot();
        StringWriter output = new();
        string newLine = output.NewLine;
        Dictionary<string, string> firstFileWithText = [];
        StringBuilder expected = new();

        AgentLegalNotices.WriteLicenses(output);

        foreach (string file in AgentLegalNotices.Files)
        {
            string text = File.ReadAllText(Path.Combine(root, file)).TrimEnd().ReplaceLineEndings(newLine);
            string body = firstFileWithText.TryGetValue(text, out string? first) ? $"The same text as {first} above." : text;
            firstFileWithText.TryAdd(text, file);
            expected.Append($"======== {file} ========{newLine}{newLine}{body}{newLine}{newLine}");
        }

        Assert.Equal(expected.ToString(), output.ToString());
        Assert.Contains(
            $"======== licenses/wimlib/COPYING.GPLv3 ========{newLine}{newLine}The same text as LICENSE above.{newLine}",
            output.ToString(),
            StringComparison.Ordinal);
    }

    // Redirected output must keep characters the console's code page lacks, like the copyright signs in the .NET
    // notices. So it has to match what WriteLicenses writes, character for character.
    [Fact]
    public async Task LicensesPrintsTheTextsAndExitsWithoutAServer()
    {
        (int exitCode, string output, string error) = await RunAgentAsync(AgentLegalNotices.LicensesArgument);
        StringWriter expected = new();
        AgentLegalNotices.WriteLicenses(expected);

        Assert.Equal(AgentExitCodes.Stopped, exitCode);
        Assert.Empty(error);
        Assert.Equal(expected.ToString(), output);
        Assert.All(AgentLegalNotices.Files, file => Assert.Contains($"======== {file} ========", output, StringComparison.Ordinal));
        Assert.Contains("GNU GENERAL PUBLIC LICENSE", output, StringComparison.Ordinal);
        Assert.Contains("GNU LESSER GENERAL PUBLIC LICENSE", output, StringComparison.Ordinal);
        Assert.DoesNotContain(s_startupLines[^1], output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PrintsTheNoticesBeforeItReadsItsArguments()
    {
        (int exitCode, string output, string error) = await RunAgentAsync();

        Assert.Equal(AgentExitCodes.ConfigurationError, exitCode);
        Assert.StartsWith(string.Join(Environment.NewLine, s_startupLines) + Environment.NewLine, output, StringComparison.Ordinal);
        Assert.StartsWith("An https server URL is required.", error, StringComparison.Ordinal);
    }

    // The build puts the agent next to the tests. Without agent.json there, it has no server to contact. A separate
    // console gets the system's OEM code page, like in WinPE, whatever console runs the tests.
    private static async Task<(int ExitCode, string Output, string Error)> RunAgentAsync(params string[] arguments)
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        ProcessStartInfo start = new(Path.Combine(AppContext.BaseDirectory, "ddt-agent.exe"))
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };

        foreach (string argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        Assert.False(File.Exists(Path.Combine(AppContext.BaseDirectory, "agent.json")));

        using Process process = Process.Start(start) ?? throw new InvalidOperationException("The agent did not start.");
        Task<string> output = process.StandardOutput.ReadToEndAsync(cancellationToken);
        Task<string> error = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);

        return (process.ExitCode, await output, await error);
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
