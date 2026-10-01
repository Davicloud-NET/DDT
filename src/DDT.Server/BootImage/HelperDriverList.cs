// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Server.BootImage;

// drivers.json, which tells Build-BootImage.ps1 what the unpacked folders next to it are.
public sealed record HelperDriverList(string? DriverSetHash, IReadOnlyList<HelperDriver> Drivers);
