// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Contracts.Agents;

// The graphical console the agents of netbooting machines switch to, file by file. Frozen like AgentRelease: agents
// inside boot images built long ago read it, so its members are never renamed or removed.
public sealed record ConsoleRelease(IReadOnlyList<ConsoleReleaseFile> Files)
{
    // What a console is made of, as ConsolePipe.Files names it for the agent: the console, first, and the two libraries
    // it draws with.
    public static IReadOnlyList<string> FileNames { get; } = ["ddt-console.exe", "libSkiaSharp.dll", "libHarfBuzzSharp.dll"];
}

public sealed record ConsoleReleaseFile(string Name, string Sha256, long Size);
