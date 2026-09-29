// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using Microsoft.Extensions.Primitives;

namespace DDT.Pxe;

// How the host learns what to serve, when that changes, and where each result goes. DDT.Pxe knows nothing about the
// settings store. The host wires these up to it.
public sealed record PxeHostSource(Func<PxeDesiredSetup> Desired, Func<IChangeToken> Changed, Func<PxeApplyResult, Task> Applied);
