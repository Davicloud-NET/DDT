// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Agent.Consoles;

// A wait at the machine that anyone there ends with a key.
public interface IKeyWait
{
    // False where nobody can press one, as with redirected input.
    bool CanPress { get; }

    // True when a key was pressed within the time. A key pressed before the wait began does not count. Throws
    // OperationCanceledException once cancelled.
    Task<bool> PressedWithinAsync(TimeSpan time, CancellationToken cancellationToken);
}
