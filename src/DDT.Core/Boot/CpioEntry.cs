// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Core.Boot;

// A folder or a file of a cpio archive. Path has forward slashes and no leading one.
public sealed record CpioEntry(string Path, ReadOnlyMemory<byte> Content, bool IsFolder)
{
    public static CpioEntry Folder(string path) => new(path, ReadOnlyMemory<byte>.Empty, true);

    public static CpioEntry File(string path, ReadOnlyMemory<byte> content) => new(path, content, false);
}
