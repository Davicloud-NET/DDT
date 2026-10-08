// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Server.Machines;

// Where the server finds one platform's agent: the file configuration names, which may be empty, the place an upload
// goes to, and the file a release puts next to the server.
public sealed record AgentSlot(AgentFormat Format, string ConfiguredPath, string UploadPath, string BundledPath)
{
    // How many of a file's first bytes tell whether it is an agent of the format.
    public int HeaderLength => Format == AgentFormat.LinuxExecutable ? ElfHeader.Length : 2;

    // Every Windows executable starts with the DOS header's MZ.
    public bool Accepts(ReadOnlySpan<byte> header) => Format switch
    {
        AgentFormat.LinuxExecutable => ElfHeader.IsX64Program(header),
        _ => header.Length >= 2 && header[0] == (byte)'M' && header[1] == (byte)'Z',
    };

    public IEmbeddedVersion NewVersion() => Format == AgentFormat.LinuxExecutable ? new ElfAgentVersion() : new ExecutableVersion();
}
