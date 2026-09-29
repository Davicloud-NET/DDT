// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Contracts.Settings;

public sealed record BootTargetSettings(
    // Tftp or Http.
    string? Method,
    string? BootFile,
    string? ServerAddress,
    string? ServerHostName,
    bool AdvertiseBootServerDiscovery);
