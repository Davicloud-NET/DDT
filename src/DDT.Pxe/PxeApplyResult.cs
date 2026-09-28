// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Messages;

namespace DDT.Pxe;

// Interfaces is what the host found when it applied, null when it built no setup. Message is English, and Text the same
// sentence as a code where it is one DDT knows, such as a port that did not bind; an exception's own text has none.
public sealed record PxeApplyResult(long Version, bool Succeeded, string? Message, NetworkInterfaceMap? Interfaces, ServerMessage? Text = null);
