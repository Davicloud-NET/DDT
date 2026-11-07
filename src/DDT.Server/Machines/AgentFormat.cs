// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Server.Machines;

// The kind of file an agent is, which decides what an upload must be and where the file's version is found.
public enum AgentFormat
{
    // ddt-agent.exe, with a version resource
    WindowsExecutable,

    // The static ddt-agent for Linux on x64, with the marker ElfAgentVersion reads
    LinuxExecutable,
}
