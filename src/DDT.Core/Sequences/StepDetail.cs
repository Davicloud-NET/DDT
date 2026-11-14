// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Core.Sequences;

// What the running step is doing now, such as the application it installs. Null clears it.
public sealed record StepDetail(Guid StepId, string? Text);
