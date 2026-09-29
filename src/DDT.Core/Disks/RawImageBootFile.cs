// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Core.Disks;

// A file from a disk image's EFI system partition. Path is the volume's name for it, such as \EFI\BOOT\BOOTX64.EFI.
public sealed record RawImageBootFile(string Path, byte[] Content);
