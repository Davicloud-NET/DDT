// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Contracts.Sequences;

// Revision is null when the sequence was deleted.
public sealed record SequenceChangedEvent(Guid Id, long? Revision, string? ChangedBy);
