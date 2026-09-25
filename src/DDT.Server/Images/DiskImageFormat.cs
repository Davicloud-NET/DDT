// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Server.Images;

// What an uploaded disk image is, from its first bytes. Raw is anything else, which the partition table then proves.
public enum DiskImageFormat
{
    Raw,
    Gzip,
    Zstd,
    Xz,
    Qcow2,
    Vhdx,
    Vmdk,
    Vdi,
}
