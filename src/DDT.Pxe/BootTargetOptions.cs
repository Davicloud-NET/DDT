// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Pxe;

// Every leaf is a string, so a bad value becomes a named failure in PxeSetup. The binder silently leaves an
// unparseable IPAddress null. And an entry made by a single environment variable would default its Method to Tftp.
public sealed class BootTargetOptions
{
    public string? Method { get; set; }

    public string? BootFile { get; set; }

    public string? ServerAddress { get; set; }

    public string? ServerHostName { get; set; }

    public bool AdvertiseBootServerDiscovery { get; set; }
}
