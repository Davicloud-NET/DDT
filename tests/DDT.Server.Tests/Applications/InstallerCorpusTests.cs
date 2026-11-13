// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Text.Json;
using DDT.Server.Applications.Installers;
using Xunit;

namespace DDT.Server.Tests.Applications;

// Real installers, which never become fixtures: DDT_TEST_INSTALLER_CORPUS names the folder of a corpus.json that lists
// each with its path and the uninstall key it is known to write, or null. A key read must be the known one; none is
// allowed.
public sealed class InstallerCorpusTests
{
    [Fact]
    public void TheUninstallKeyOfARealInstallerIsTheOneItWritesOrNone()
    {
        string? folder = Environment.GetEnvironmentVariable("DDT_TEST_INSTALLER_CORPUS");
        Assert.SkipWhen(string.IsNullOrEmpty(folder), "DDT_TEST_INSTALLER_CORPUS names no installer corpus.");

        using JsonDocument corpus = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(folder, "corpus.json")));
        List<string> wrong = [];

        foreach (JsonElement entry in corpus.RootElement.EnumerateArray())
        {
            string name = entry.GetProperty("name").GetString() ?? "";
            string? expected = entry.GetProperty("expectedKey").GetString();
            string path = Locate(folder, entry.GetProperty("path").GetString() ?? "");

            using FileStream file = File.OpenRead(path);
            InstallerFacts facts = InstallerProbe.Read(file);
            string verdict = facts.UninstallKey is null ? "none" : expected is null ? "read" : facts.UninstallKey == expected ? "match" : "WRONG";
            TestContext.Current.TestOutputHelper?.WriteLine($"{name,-12} {facts.Kind,-11} expected {expected ?? "-",-48} read {facts.UninstallKey ?? "-",-48} {verdict}");

            if (verdict == "WRONG")
            {
                wrong.Add(name);
            }
        }

        Assert.Empty(wrong);
    }

    // The paths are relative to the corpus's root, which is the folder or one above it.
    private static string Locate(string folder, string path)
    {
        for (DirectoryInfo? directory = new(folder); directory is not null; directory = directory.Parent)
        {
            string candidate = Path.Combine(directory.FullName, path);

            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new FileNotFoundException($"{path} is not under {folder} or a folder above it.");
    }
}
