// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Contracts.Agents;

// The files of the graphical console that the agents of netbooting machines switch to. Frozen like AgentRelease.
// Agents inside old boot images read it, so its members are never renamed or removed.
public sealed record ConsoleRelease(IReadOnlyList<ConsoleReleaseFile> Files)
{
    // The files of a console, named the way ConsolePipe.Files names them for the agent. The console comes first, then
    // the two libraries it draws with.
    public static IReadOnlyList<string> FileNames { get; } = ["ddt-console.exe", "libSkiaSharp.dll", "libHarfBuzzSharp.dll"];
}
