// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Agent.Deployment;

// The tools that write another code page than ANSI into a pipe, by file name.
public static class WindowsToolDecoders
{
    public static IReadOnlyDictionary<string, ToolOutputDecoder> All { get; } =
        new Dictionary<string, ToolOutputDecoder>(StringComparer.OrdinalIgnoreCase) { ["dism.exe"] = AnsiCodePage.DecodeOem };
}
