// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Core.Templates;

// Parse reports the first four: a name the caller does not know, and filters that are not written as DDT's filters
// are. Rendering reports a filter problem too, and a name without a value.
public enum TemplateProblemKind
{
    UnknownName,
    UnknownFilter,
    FilterNeedsCount,
    FilterTakesNoCount,
    MissingValue,
}
