// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Agents;

namespace DDT.Server.Machines;

// Where the server finds one platform's console, as AgentSlot does for its agent, and the files it is made of.
public sealed record ConsoleSlot(AgentFormat Format, string ConfiguredPath, string UploadPath, string BundledPath)
{
    // The console's executable first, then the two libraries it draws with.
    public IReadOnlyList<string> FileNames => Format == AgentFormat.LinuxExecutable ? ConsoleRelease.LinuxFileNames : ConsoleRelease.FileNames;

    public int HeaderLength => Format == AgentFormat.LinuxExecutable ? ElfHeader.Length : 2;

    // Each file, the libraries too, is an x64 program or library of the console's system.
    public bool Accepts(ReadOnlySpan<byte> header) => Format switch
    {
        AgentFormat.LinuxExecutable => ElfHeader.IsX64Program(header),
        _ => header.Length >= 2 && header[0] == (byte)'M' && header[1] == (byte)'Z',
    };

    public IEmbeddedVersion NewVersion() =>
        Format == AgentFormat.LinuxExecutable ? new ElfAgentVersion(ElfAgentVersion.ConsoleMarker) : new ExecutableVersion();
}
