// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Server.Applications.Installers;

// A directory entry of a compound file: a storage, a stream or the root.
internal sealed record CompoundFileEntry(string Name, byte Type, uint Left, uint Right, uint Child, uint StartSector, long Size)
{
    public const byte StreamType = 2;
    public const byte RootType = 5;
}
