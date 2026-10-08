// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Collections.Frozen;
using DDT.Contracts.Images;
using DDT.Contracts.Messages;
using DDT.Pxe;

namespace DDT.Server.LinuxBoot;

// What the server found of the Linux boot environment. Problem says why no machine can be started in it, and is null
// when one can. The rest is what its description says, as far as it could be read.
public sealed record LinuxBootState(string Folder, ServerMessage? Problem)
{
    public bool Available => Problem is null;

    // The names in Folder that netboot may serve, each with what the file was when the server looked at it.
    public IReadOnlyDictionary<string, BootFileStamp> Files { get; init; } = FrozenDictionary<string, BootFileStamp>.Empty;

    public string? Snapshot { get; init; }

    public string? KernelRelease { get; init; }

    public string? ShimVersion { get; init; }

    public string? GrubVersion { get; init; }

    // Which of Microsoft's third-party UEFI CAs the shim is signed under, as the server read its signatures. Null
    // when it found none of them.
    public UefiCa? ShimSignedUnder { get; init; }

    public IReadOnlyList<string> Keymaps { get; init; } = [];
}
