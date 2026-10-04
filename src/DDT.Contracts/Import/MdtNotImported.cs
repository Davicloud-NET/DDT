// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Contracts.Import;

// What DDT has no place for yet: the share's applications and task sequences by name, and the sections of
// CustomSettings.ini.
public sealed record MdtNotImported(IReadOnlyList<string> Applications, IReadOnlyList<string> TaskSequences, IReadOnlyList<string> Rules);
