// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Agent;

// One of a run's images or packages, by its SHA-256, from Offset to the end.
public sealed record RunFileRange(Guid RunId, string Sha256, long Offset);
