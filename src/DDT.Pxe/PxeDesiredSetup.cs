// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Messages;

namespace DDT.Pxe;

// What the host should serve. When Options is null, Refusal says why. StopHostOnFailure means configuration decided
// the setup, so a bind failure at startup stops the host instead of being reported.
public sealed record PxeDesiredSetup(long Version, PxeOptions? Options, ServerMessage? Refusal, bool StopHostOnFailure);
