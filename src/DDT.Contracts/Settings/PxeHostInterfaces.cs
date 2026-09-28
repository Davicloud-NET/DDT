// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Contracts.Settings;

// The interfaces a host that runs the pxe role found when it last applied the section. Unmatched lists the entries of
// interfaces that name nothing on that host.
public sealed record PxeHostInterfaces(string Host, DateTimeOffset? UpdatedUtc, IReadOnlyList<PxeInterface> Interfaces, IReadOnlyList<string> Unmatched);
