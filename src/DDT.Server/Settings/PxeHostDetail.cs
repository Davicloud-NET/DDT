// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Server.Settings;

// What a pxe host found when it applied the section, kept in SettingsHostState.Detail. Unmatched are the entries of
// Interfaces that name no interface there.
internal sealed record PxeHostDetail(IReadOnlyList<PxeHostCandidate> Candidates, IReadOnlyList<string> Unmatched);
