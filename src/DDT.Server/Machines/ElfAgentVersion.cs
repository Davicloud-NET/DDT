// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Text;

namespace DDT.Server.Machines;

// The version of the agent for Linux, found while its bytes pass by for hashing. An ELF file has no version resource,
// so the agent's build writes a marker into it: DDT-AGENT-VERSION:, then the version, up to the line's end.
public sealed class ElfAgentVersion : IEmbeddedVersion
{
    // Year.Major.Build takes 13 characters at most. More than this after the marker is not a version.
    private const int MaxVersionBytes = 32;

    private static readonly byte[] s_marker = "DDT-AGENT-VERSION:"u8.ToArray();

    // The end of the last block, where a marker may have begun
    private byte[] _carry = [];

    public Version? Found { get; private set; }

    public static Version? Of(ReadOnlySpan<byte> executable)
    {
        ElfAgentVersion version = new();
        version.Append(executable);

        return version.Found;
    }

    public void Append(ReadOnlySpan<byte> block)
    {
        if (Found is not null)
        {
            return;
        }

        byte[] joined = [.. _carry, .. block];
        int from = 0;

        while (joined.AsSpan(from).IndexOf(s_marker) is >= 0 and int found)
        {
            int at = from + found;
            ReadOnlySpan<byte> after = joined.AsSpan(at + s_marker.Length);
            int end = after.IndexOfAny((byte)'\r', (byte)'\n');

            // The line's end has not passed by yet.
            if (end < 0 && after.Length <= MaxVersionBytes)
            {
                _carry = joined[at..];

                return;
            }

            if (end is > 0 and <= MaxVersionBytes && Parse(after[..end]) is { } version)
            {
                Found = version;

                return;
            }

            // The marker's own text, as the program that looks for it holds it, is followed by no version.
            from = at + s_marker.Length;
        }

        _carry = joined[^Math.Min(joined.Length - from, s_marker.Length - 1)..];
    }

    // Digits and dots alone, as the build writes them. Version.Parse would also take spaces and signs.
    private static Version? Parse(ReadOnlySpan<byte> text)
    {
        foreach (byte character in text)
        {
            if (character != (byte)'.' && !char.IsAsciiDigit((char)character))
            {
                return null;
            }
        }

        return Version.TryParse(Encoding.ASCII.GetString(text), out Version? version) ? version : null;
    }
}
