// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Server.LinuxBoot;

// What ddt-linux-boot.json, written by build/LinuxBoot/manifest.py, says about the Linux boot environment beside it.
internal sealed class LinuxBootManifest
{
    public int Schema { get; set; }

    // The snapshot.debian.org time its packages are pinned at.
    public string? Snapshot { get; set; }

    public string? KernelRelease { get; set; }

    // The certificate authorities the shim's signatures lead to, by their names.
    public List<string>? ShimSignedUnder { get; set; }

    public List<LinuxBootManifestFile>? Files { get; set; }

    public List<LinuxBootManifestPackage>? Packages { get; set; }

    public LinuxBootManifestBaseImage? BaseImage { get; set; }
}
