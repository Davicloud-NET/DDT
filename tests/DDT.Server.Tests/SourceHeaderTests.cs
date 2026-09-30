// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using Xunit;

namespace DDT.Server.Tests;

// NOTICE's attribution term points at the copyright notices in the source files.
// So every source file has to start with the licence header. IDE0073 enforces it for C# at build time.
// This test also covers the web client, the console's XAML and the scripts.
public sealed class SourceHeaderTests
{
    private static readonly string[] s_headerLines =
    [
        "Copyright (C) 2026 Davicloud",
        "SPDX-License-Identifier: GPL-3.0-or-later",
        "Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.",
    ];

    // Each line of the header sits between these, per file type. CSS has only block comments.
    private static readonly Dictionary<string, (string Prefix, string Suffix)> s_comments = new(StringComparer.Ordinal)
    {
        [".cs"] = ("// ", ""),
        [".ts"] = ("// ", ""),
        [".tsx"] = ("// ", ""),
        [".js"] = ("// ", ""),
        [".mjs"] = ("// ", ""),
        [".css"] = ("/* ", " */"),
        [".ps1"] = ("# ", ""),
        [".psm1"] = ("# ", ""),
        [".psd1"] = ("# ", ""),
        [".py"] = ("# ", ""),
        [".axaml"] = ("<!-- ", " -->"),
        [".wxs"] = ("<!-- ", " -->"),
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
        Assert.Contains(Path.Combine(root, "src", "DDT.Web", "src", "styles", "app.css"), files);
        Assert.Contains(Path.Combine(root, "src", "DDT.MachineConsole", "Views", "MainWindow.axaml"), files);

        List<string> withoutHeader = [];
        foreach (string file in files)
        {
            if (!await StartsWithHeaderAsync(file, s_comments[Path.GetExtension(file)], cancellationToken))
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
            if (s_comments.ContainsKey(file.Extension))
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

    private static async Task<bool> StartsWithHeaderAsync(string path, (string Prefix, string Suffix) comment, CancellationToken cancellationToken)
    {
        using StreamReader reader = new(path);

        foreach (string line in s_headerLines)
        {
            if (await reader.ReadLineAsync(cancellationToken) != comment.Prefix + line + comment.Suffix)
            {
                return false;
            }
        }

        return true;
    }
}
