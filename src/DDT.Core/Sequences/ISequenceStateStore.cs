// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Sequences;

namespace DDT.Core.Sequences;

// The state has to be durable when SaveAsync returns: the engine resumes from the last saved state after a restart.
public interface ISequenceStateStore
{
    Task SaveAsync(SequenceState state, CancellationToken cancellationToken);
}
