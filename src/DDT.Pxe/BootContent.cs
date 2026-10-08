// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Pxe;

// What a boot file request is answered with: a file on disk, or bytes made for the request.
public abstract class BootContent
{
    // What the log names: the file's path, or what was generated.
    public abstract string Description { get; }

    // Throws IOException or UnauthorizedAccessException when a file cannot be opened.
    public abstract BootContentReader OpenRead();
}
