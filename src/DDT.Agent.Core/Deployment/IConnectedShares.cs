// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Agent.Deployment;

// A step's shares while they are connected. Disposing disconnects them all and never throws.
public interface IConnectedShares : IAsyncDisposable
{
    // Where the step finds its shares, in their order, when the system gives a share a place of its own. Empty when
    // a share is reached by the path it has, as in Windows.
    IReadOnlyList<string> LocalPaths { get; }
}
