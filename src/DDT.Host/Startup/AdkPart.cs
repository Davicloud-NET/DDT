// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Host.Startup;

// One of Microsoft's two setups. Marker is a file of the feature, below the kit's folder.
public sealed record AdkPart(string Name, string Version, string File, Uri Address, string Sha256, string Feature, string Marker);
