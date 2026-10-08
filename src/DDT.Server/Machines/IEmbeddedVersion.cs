// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Server.Machines;

// A version a file carries, found while the file's bytes pass by for hashing.
public interface IEmbeddedVersion
{
    Version? Found { get; }

    void Append(ReadOnlySpan<byte> block);
}
