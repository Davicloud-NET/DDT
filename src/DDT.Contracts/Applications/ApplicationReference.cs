// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Contracts.Applications;

// An application or a bundle, as a step or an input names it.
public sealed record ApplicationReference(Guid Id, ApplicationReferenceKind Kind);
