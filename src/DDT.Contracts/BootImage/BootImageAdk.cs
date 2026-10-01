// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Contracts.BootImage;

// The Windows ADK on the server. Version is that of its Windows PE add-on, which boot.wim comes from. Supported is
// false for an add-on older than the build script takes.
public sealed record BootImageAdk(bool Installed, string? Version, bool Supported);
