// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Images;

namespace DDT.Server.Images;

// Whether a raw disk image starts with Secure Boot on, the sentence that says why, and the processor its boot file
// is for, null when it has none DDT could read.
public sealed record BootAssessment(ImageBootCapability Capability, string Detail, string? Architecture);
