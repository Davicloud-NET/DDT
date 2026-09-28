// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Core.Disks;

// A file or directory for FatVolumeBuilder to lay out. Nodes are laid out in the order they were added.
internal sealed class FatBuildNode(string name, byte[]? content)
{
    public string Name { get; } = name;

    // Null for a directory.
    public byte[]? Content { get; } = content;

    public bool IsDirectory => Content is null;

    public List<FatBuildNode> Children { get; } = [];

    // The 11 bytes of the short entry. HasLongName says whether long name entries go before it.
    public byte[] ShortName { get; set; } = [];

    public bool HasLongName { get; set; }

    public uint FirstCluster { get; set; }

    public int Clusters { get; set; }

    // A directory's entries, once laid out.
    public byte[] Entries { get; set; } = [];
}
