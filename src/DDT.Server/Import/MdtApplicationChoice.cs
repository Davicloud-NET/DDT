// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Server.Import;

// What to import of a share's applications: Entries is the whole list, for dependencies on entries not Chosen.
public sealed record MdtApplicationChoice(string Share, IReadOnlyList<MdtShareApplication> Entries, IReadOnlyList<MdtShareApplication> Chosen);
