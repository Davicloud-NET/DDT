// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using Xunit;

namespace DDT.Server.Tests;

// NOTICE's attribution term points at the copyright notices in the source files, so every source file has to start
// with the licence header. IDE0073 enforces it for C# at build time; this also covers the web client and the scripts.
public sealed class SourceHeaderTests
{
    private static readonly string[] s_headerLines =
    [
        "Copyright (C) 2026 Davicloud",
        "SPDX-License-Identifier: GPL-3.0-or-later",
        "Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.",
    ];

    private static readonly Dictionary<string, string> s_commentPrefixes = new(StringComparer.Ordinal)
    {
        [".cs"] = "// ",
        [".ts"] = "// ",
        [".tsx"] = "// ",
        [".js"] = "// ",
        [".scss"] = "// ",
        [".ps1"] = "# ",
    };

    private static readonly string[] s_sourceFolders = ["src", "tests", "build"];

    // Build output, installed packages and the bundle Vite writes into the host are not source.
    private static readonly HashSet<string> s_skippedFolders = new(StringComparer.Ordinal) { "bin", "obj", "node_modules", "wwwroot" };

    [Fact]
    public async Task EverySourceFileStartsWithTheLicenceHeader()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        string root = Repository.Root();

        List<string> files = [.. s_sourceFolders.SelectMany(folder => SourceFiles(new DirectoryInfo(Path.Combine(root, folder))))];

        Assert.Contains(Path.Combine(root, "build", "Publish-Agent.ps1"), files);
        Assert.Contains(Path.Combine(root, "src", "DDT.Web", "src", "main.tsx"), files);

        List<string> withoutHeader = [];
        foreach (string file in files)
        {
            if (!await StartsWithHeaderAsync(file, s_commentPrefixes[Path.GetExtension(file)], cancellationToken))
            {
                withoutHeader.Add(Path.GetRelativePath(root, file));
            }
        }

        Assert.Empty(withoutHeader);
    }

    private static IEnumerable<string> SourceFiles(DirectoryInfo directory)
    {
        foreach (FileInfo file in directory.EnumerateFiles())
        {
            if (s_commentPrefixes.ContainsKey(file.Extension))
            {
                yield return file.FullName;
            }
        }

        foreach (DirectoryInfo child in directory.EnumerateDirectories().Where(child => !s_skippedFolders.Contains(child.Name)))
        {
            foreach (string file in SourceFiles(child))
            {
                yield return file;
            }
        }
    }

    private static async Task<bool> StartsWithHeaderAsync(string path, string commentPrefix, CancellationToken cancellationToken)
    {
        using StreamReader reader = new(path);

        foreach (string line in s_headerLines)
        {
            if (await reader.ReadLineAsync(cancellationToken) != commentPrefix + line)
            {
                return false;
            }
        }

        return true;
    }
}
