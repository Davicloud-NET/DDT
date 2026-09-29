// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Images;

namespace DDT.Server.Images;

// Says whether a raw disk image starts with Secure Boot on, with a sentence that explains why. Architecture is the
// processor its boot file is for. It's null when the image has no boot file DDT could read.
public sealed record BootAssessment(ImageBootCapability Capability, string Detail, string? Architecture, UefiCa? SignedUnder = null);
