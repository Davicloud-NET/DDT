// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.E2E;

// The repository these tests were built in, and the host built with them: the project reference builds it into its
// own bin folder, under the same configuration and framework as this one.
internal static class RepositoryPaths
{
    public static string Root { get; } = FindRoot();

    public static string Host { get; } = Path.Combine(
        Root,
        "src",
        "DDT.Host",
        Path.GetRelativePath(Path.Combine(Root, "tests", "DDT.E2E"), AppContext.BaseDirectory),
        "DDT.Host.exe");

    public static string PublishAgentScript { get; } = Path.Combine(Root, "build", "Publish-Agent.ps1");

    private static string FindRoot()
    {
        for (DirectoryInfo? directory = new(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "DDT.slnx")))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException($"No DDT.slnx above {AppContext.BaseDirectory}: the tests have to run from their build output in the repository.");
    }
}
