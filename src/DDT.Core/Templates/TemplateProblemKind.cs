// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Core.Templates;

// Parse reports the first four, which are a name the caller doesn't know and badly written filters. Rendering also
// reports filter problems, and a name without a value.
public enum TemplateProblemKind
{
    UnknownName,
    UnknownFilter,
    FilterNeedsCount,
    FilterTakesNoCount,
    MissingValue,
}
