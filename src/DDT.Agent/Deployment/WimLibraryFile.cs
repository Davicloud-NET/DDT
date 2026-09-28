// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Security.Cryptography;

namespace DDT.Agent.Deployment;

// The agent carries libwim-15.dll as a resource, so the boot image and the self-update only deliver one executable.
// The runtime looks for the library next to the executable, so that's where it's written.
public static class WimLibraryFile
{
    public const string FileName = "libwim-15.dll";

    // Only writes the carried library when there's none. A library that's already there is used as it is, even when
    // it differs. wimlib's licence, the LGPL, lets users run the agent with a libwim they built themselves. Must run
    // before the first wimlib call, because a loaded library can't be replaced.
    public static WimLibraryInUse EnsureExtracted(string directory)
    {
        ArgumentException.ThrowIfNullOrEmpty(directory);

        string path = Path.GetFullPath(Path.Combine(directory, FileName));
        byte[] library = ReadResource();
        string carriedSha256 = Convert.ToHexStringLower(SHA256.HashData(library));

        if (File.Exists(path))
        {
            return new WimLibraryInUse(path, Sha256(path), carriedSha256);
        }

        Directory.CreateDirectory(directory);

        string partial = path + ".part";
        File.WriteAllBytes(partial, library);
        File.Move(partial, path);

        return new WimLibraryInUse(path, carriedSha256, carriedSha256);
    }

    private static string Sha256(string path)
    {
        using FileStream file = new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);

        return Convert.ToHexStringLower(SHA256.HashData(file));
    }

    private static byte[] ReadResource()
    {
        using Stream resource = typeof(WimLibraryFile).Assembly.GetManifestResourceStream(FileName)
            ?? throw new InvalidOperationException($"This agent was built without {FileName}.");
        using MemoryStream copy = new();
        resource.CopyTo(copy);

        return copy.ToArray();
    }
}
