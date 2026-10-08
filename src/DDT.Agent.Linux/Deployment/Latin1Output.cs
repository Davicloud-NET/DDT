// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Text;

namespace DDT.Agent.Deployment;

// Reads a tool's line that is not UTF-8. Linux has no code page to ask for, and Latin-1 gives every byte a character.
public static class Latin1Output
{
    public static string Decode(ReadOnlySpan<byte> bytes) => Encoding.Latin1.GetString(bytes);
}
