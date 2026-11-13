// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Contracts.Applications;

// Creates or saves a bundle. Revision is ignored for a new one.
public sealed record SaveApplicationBundleRequest(long Revision, string Name, string? Description, IReadOnlyList<Guid> Members);
