// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Buffers.Binary;
using System.Text;

namespace DDT.Server.Applications.Installers;

// Tells which tool built an installer EXE from the marks each one leaves in the stub or at the start of the overlay.
// Only bounded windows are read.
internal static class ExecutableInstallerMarkers
{
    private const int OverlayWindow = 64 * 1024;
    private const int InstallShieldStreamWindow = 8 * 1024;
    private const uint BurnSectionMagic = 0x00F14300;

    // WiX burn/engine/section.cpp: magic, version, then the bundle's GUID.
    private const string BurnSection = ".wixburn";

    // NSIS fileform.h: the firstheader's FH_SIG, then "NullsoftInst" (some builds change the fifth letter).
    private static readonly byte[] s_nsisSignature = [0xEF, 0xBE, 0xAD, 0xDE, (byte)'N', (byte)'u', (byte)'l', (byte)'l'];

    // Inno Setup's SetupLdrOffsetTableID, in resource 11111 of the loader.
    private static readonly byte[] s_innoOffsetTable = "rDlPtS"u8.ToArray();

    private static readonly byte[] s_installShieldStream = "ISSetupStream"u8.ToArray();
    private static readonly byte[] s_installShieldHeader = "InstallShield"u8.ToArray();
    private static readonly byte[] s_installShieldVersionKey = Encoding.Unicode.GetBytes("ISInternalVersion");

    public static (InstallerKind Kind, string? BundleCode) Recognize(Stream file, PeLayout layout, ReadOnlySpan<byte> resources)
    {
        if (BurnBundleCode(file, layout) is { } bundleCode)
        {
            return (InstallerKind.Burn, bundleCode);
        }

        byte[] overlay = file.ReadAtMost(layout.OverlayOffset, (int)Math.Min(layout.OverlayLength, OverlayWindow));

        if (IsNsis(overlay))
        {
            return (InstallerKind.Nsis, null);
        }

        if (resources.IndexOf(s_innoOffsetTable) >= 0)
        {
            return (InstallerKind.InnoSetup, null);
        }

        bool installShield = overlay.AsSpan(0, Math.Min(overlay.Length, InstallShieldStreamWindow)).IndexOf(s_installShieldStream) >= 0
            || overlay.AsSpan().StartsWith(s_installShieldHeader)
            || resources.IndexOf(s_installShieldVersionKey) >= 0;

        return (installShield ? InstallerKind.InstallShield : InstallerKind.Unknown, null);
    }

    private static string? BurnBundleCode(Stream file, PeLayout layout)
    {
        if (layout.Section(BurnSection) is not { RawLength: >= 24 } section)
        {
            return null;
        }

        byte[] header = file.ReadAtMost(section.RawOffset, 24);

        return header.Length == 24 && BinaryPrimitives.ReadUInt32LittleEndian(header) == BurnSectionMagic
            ? new Guid(header.AsSpan(8, 16)).ToString("B").ToUpperInvariant()
            : null;
    }

    private static bool IsNsis(ReadOnlySpan<byte> overlay)
    {
        int at = overlay.IndexOf(s_nsisSignature);

        return at >= 0 && overlay.Length >= at + 16 && overlay.Slice(at + 9, 7).SequenceEqual("oftInst"u8);
    }
}
