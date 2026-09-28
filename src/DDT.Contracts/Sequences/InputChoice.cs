// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Contracts.Sequences;

// Label is what the person sees, and Value is what the answer sets. A null label shows the value.
public sealed record InputChoice(string Value, string? Label = null);
