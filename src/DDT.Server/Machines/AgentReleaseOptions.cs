// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Server.Machines;

public sealed class AgentReleaseOptions
{
    public const string SectionName = "DDT:Agent";

    // The published agent every netbooting machine switches to. Empty means agent/ddt-agent.exe in the store.
    public string BinaryPath { get; set; } = string.Empty;

    // The graphical console those agents switch to, as a zip with the files Publish-Console.ps1 writes at its root. Empty
    // means agent/ddt-console.zip in the store.
    public string ConsolePath { get; set; } = string.Empty;
}
