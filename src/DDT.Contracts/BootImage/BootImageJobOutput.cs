// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Contracts.BootImage;

// Lines a running job wrote, pushed as they come. First is the index of the first one, so a page that missed some
// loads the log again instead of showing a gap.
public sealed record BootImageJobOutput(DateTimeOffset StartedUtc, int First, IReadOnlyList<string> Lines);
