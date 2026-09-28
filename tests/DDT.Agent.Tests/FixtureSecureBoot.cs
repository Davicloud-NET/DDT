// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Images;

namespace DDT.Agent.Tests;

// What the firmware says of Secure Boot and the CAs it trusts, and whether the run may write an image that will not
// start with it.
internal sealed record FixtureSecureBoot(bool? Enabled = null, UefiCa? TrustedUefiCas = null, bool MismatchAllowed = false);
