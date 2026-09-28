// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Server.Settings;

// What a PXE host found when it applied the section. It's kept in SettingsHostState.Detail. Unmatched lists the entries
// of Interfaces that don't match any interface on that host.
internal sealed record PxeHostDetail(IReadOnlyList<PxeHostCandidate> Candidates, IReadOnlyList<string> Unmatched);
