// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Images;
using DDT.Contracts.Messages;

namespace DDT.Contracts.BootImage;

// The Linux boot environment a release of DDT brings. Problem says in English why no machine can be started in it, and
// ProblemText holds the same sentence as a code with its values. Both are null when machines can be started.
public sealed record LinuxBootView(
    bool Available,
    string? Problem,
    ServerMessage? ProblemText,
    // The snapshot.debian.org time its packages are pinned at.
    string? Snapshot,
    string? Kernel,
    string? Shim,
    string? Grub,
    // Which of Microsoft's third-party UEFI CAs the shim is signed under. A machine with Secure Boot on starts it when
    // its firmware trusts one of them. Null when the server found none.
    UefiCa? ShimSignedUnder,
    // The keyboard layouts its text console has, by the names ConsoleKeyboardLayout takes.
    IReadOnlyList<string> Keymaps,
    // The boot file a machine that starts it loads, as ProxyDHCP names it.
    string BootFile);
