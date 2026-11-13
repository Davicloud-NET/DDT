// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Contracts.Applications;

// Something that names an application or a bundle, so it cannot be deleted.
public sealed record ApplicationUse(Guid Id, string Name, ApplicationUseKind Kind);
